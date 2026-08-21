//! Writing the diagnostics zip.
//!
//! The bundle is written straight to a file in the host-supplied destination,
//! never handed back across the FFI boundary. Returning the bytes would hold the
//! whole archive twice - once in a Rust boxed slice, once in a C# `byte[]` - in
//! a 32-bit process whose address space is already the reason redb's page cache
//! is capped at 64 MiB.

use std::fs::File;
use std::io::{self, Read, Seek, SeekFrom, Write};
use std::path::{Path, PathBuf};

use serde_json::Value;
use time::OffsetDateTime;
use zip::write::SimpleFileOptions;
use zip::{CompressionMethod, ZipWriter};

/// What the bundle is built from. Assembled by the caller so this module never
/// has to reach into the core.
pub struct Inputs<'a> {
    /// The core's storage directory - where the logs live.
    pub storage: &'a str,
    /// Where the zip goes. Created if it does not exist.
    pub destination_dir: &'a str,
    /// The assembled `report.json` body.
    pub report: Value,
    /// Byte offset into the active log where the capture began, so the bundle
    /// carries the reproduction and not the user's whole history.
    pub log_offset: u64,
    /// When the capture began, Unix epoch milliseconds (UTC). Bounds which of
    /// the other log files come along; see [`extra_logs`].
    pub started_unix_ms: i64,
}

/// The note written beside a window that had to fall back to the whole file.
const ROTATED_NOTE: &[u8] = b"The active log rotated during this capture, so capture.log holds \
the whole current log file rather than only the captured window. The earlier part of the \
window is in the rolled generations under logs/.\r\n";

/// Build the bundle and return the path written.
pub fn write(inputs: Inputs<'_>) -> Result<PathBuf, String> {
    let dir = Path::new(inputs.destination_dir);
    std::fs::create_dir_all(dir).map_err(|e| format!("create {}: {e}", dir.display()))?;
    let path = dir.join(file_name());

    let file = File::create(&path).map_err(|e| format!("create {}: {e}", path.display()))?;
    let mut zip = ZipWriter::new(file);
    let options = SimpleFileOptions::default().compression_method(CompressionMethod::Deflated);

    let report = serde_json::to_vec_pretty(&inputs.report).map_err(|e| e.to_string())?;
    add_bytes(&mut zip, "report.json", &report, options)?;

    let (window, rotated) = capture_window(inputs.storage, inputs.log_offset);
    add_bytes(&mut zip, "capture.log", &window, options)?;
    if rotated {
        // Say so in the bundle rather than only in the log: a reader who finds
        // the window starting mid-session should know why.
        add_bytes(&mut zip, "capture.log.note.txt", ROTATED_NOTE, options)?;
    }

    for source in extra_logs(inputs.storage, inputs.started_unix_ms) {
        let Some(name) = source.file_name().and_then(|n| n.to_str()) else {
            continue;
        };
        // A log that vanished mid-bundle (a roll landing right now) is skipped,
        // not fatal - the bundle is still worth producing.
        let _ = add_file(&mut zip, &format!("logs/{name}"), &source, options);
    }

    zip.finish().map_err(|e| format!("finish zip: {e}"))?;
    Ok(path)
}

/// `mbrc-diagnostics-<utc>.zip`. Second resolution is enough - two captures in
/// the same second are not a case worth carrying a counter for.
fn file_name() -> String {
    let now = OffsetDateTime::now_utc();
    format!(
        "mbrc-diagnostics-{:04}{:02}{:02}-{:02}{:02}{:02}.zip",
        now.year(),
        now.month() as u8,
        now.day(),
        now.hour(),
        now.minute(),
        now.second()
    )
}

/// The slice of the active log written since the capture began, plus whether the
/// slice had to fall back to the whole file.
///
/// A log shorter than the recorded offset means it rotated mid-capture: the
/// bytes the offset pointed at are now in a gzipped generation, so the honest
/// answer is the whole current file plus a note, not a slice into a file that no
/// longer holds the window.
fn capture_window(storage: &str, offset: u64) -> (Vec<u8>, bool) {
    let path = crate::logging::active_log_path(storage);
    let Ok(mut file) = File::open(&path) else {
        return (Vec::new(), false);
    };
    let len = file.metadata().map(|m| m.len()).unwrap_or(0);
    let rotated = len < offset;
    let start = if rotated { 0 } else { offset };
    if file.seek(SeekFrom::Start(start)).is_err() {
        return (Vec::new(), rotated);
    }
    let mut buf = Vec::new();
    let _ = file.read_to_end(&mut buf);
    (buf, rotated)
}

/// The other log files that overlap the capture window: rolled generations, and
/// the host-side bootstrap logs.
///
/// Bounded by modification time rather than taken wholesale. A rolled generation
/// only belongs here if the roll happened *during* the capture - then part of the
/// window really is inside it, which is what `capture.log.note.txt` points at.
/// One untouched since before the capture is the user's back catalogue: it
/// predates the reproduction, it was written at the normal log level so it holds
/// no wire detail anyway, and it is the one part of the bundle nobody can review
/// before sending it (a gzip inside a zip is not something you can eyeball).
/// Shipping it by default would undo the slicing `capture.log` exists to do.
///
/// The same rule picks up `mbrc-bootstrap.log` exactly when the capture spanned a
/// restart - which is the case a startup bug needs and no other case does.
fn extra_logs(storage: &str, started_unix_ms: i64) -> Vec<PathBuf> {
    let active = crate::logging::active_log_path(storage);
    let mut paths: Vec<PathBuf> = crate::logging::log_files(storage)
        .into_iter()
        // The active log is already in the bundle as capture.log.
        .filter(|path| path != &active)
        .collect();
    for name in ["mbrc-bootstrap.log", "mbrc.log"] {
        let path = Path::new(storage).join(name);
        if path.is_file() {
            paths.push(path);
        }
    }
    paths
        .into_iter()
        .filter(|path| touched_since(path, started_unix_ms))
        .collect()
}

/// Whether `path` was last written at or after `unix_ms`.
///
/// A file whose timestamp cannot be read is included: the failure modes worth
/// optimizing for are "the maintainer is missing context", not "the bundle is
/// 900 KB smaller".
fn touched_since(path: &Path, unix_ms: i64) -> bool {
    let Ok(modified) = std::fs::metadata(path).and_then(|meta| meta.modified()) else {
        return true;
    };
    match modified.duration_since(std::time::UNIX_EPOCH) {
        Ok(since_epoch) => since_epoch.as_millis() as i64 >= unix_ms,
        // Before the epoch: a nonsense timestamp, so fall back to including it.
        Err(_) => true,
    }
}

fn add_bytes(
    zip: &mut ZipWriter<File>,
    name: &str,
    bytes: &[u8],
    options: SimpleFileOptions,
) -> Result<(), String> {
    zip.start_file(name, options)
        .map_err(|e| format!("start {name}: {e}"))?;
    zip.write_all(bytes)
        .map_err(|e| format!("write {name}: {e}"))
}

fn add_file(
    zip: &mut ZipWriter<File>,
    name: &str,
    source: &Path,
    options: SimpleFileOptions,
) -> Result<(), String> {
    let mut input = File::open(source).map_err(|e| format!("open {}: {e}", source.display()))?;
    zip.start_file(name, options)
        .map_err(|e| format!("start {name}: {e}"))?;
    io::copy(&mut input, zip).map_err(|e| format!("copy {name}: {e}"))?;
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    /// A scratch storage dir seeded with an active log and one rolled generation.
    fn seed(case: &str, log_body: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("mbrc-bundle-{case}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("create scratch dir");
        std::fs::write(dir.join("mbrc-core.log"), log_body).expect("seed log");
        std::fs::write(dir.join("mbrc-core.1.log.gz"), b"stand-in for a rolled log")
            .expect("seed rolled log");
        dir
    }

    fn entries(zip_path: &Path) -> Vec<String> {
        let file = File::open(zip_path).expect("open bundle");
        let mut archive = zip::ZipArchive::new(file).expect("read bundle");
        (0..archive.len())
            .map(|i| archive.by_index(i).expect("entry").name().to_owned())
            .collect()
    }

    fn read_entry(zip_path: &Path, name: &str) -> String {
        let file = File::open(zip_path).expect("open bundle");
        let mut archive = zip::ZipArchive::new(file).expect("read bundle");
        let mut entry = archive.by_name(name).expect("entry present");
        let mut body = String::new();
        entry.read_to_string(&mut body).expect("read entry");
        body
    }

    #[test]
    fn bundle_holds_the_report_the_window_and_overlapping_rolled_logs() {
        let dir = seed("contents", "before the capture\nafter the capture\n");
        let out = dir.join("out");
        let path = write(Inputs {
            storage: dir.to_str().expect("utf8 dir"),
            destination_dir: out.to_str().expect("utf8 dir"),
            report: serde_json::json!({ "versions": { "core": "1.5.0" } }),
            log_offset: "before the capture\n".len() as u64,
            // Epoch: everything in the scratch dir counts as written during the
            // capture, which is the overlap this case is about.
            started_unix_ms: 0,
        })
        .expect("bundle written");

        let names = entries(&path);
        assert!(names.contains(&"report.json".to_owned()), "{names:?}");
        assert!(names.contains(&"capture.log".to_owned()), "{names:?}");
        assert!(
            names.contains(&"logs/mbrc-core.1.log.gz".to_owned()),
            "{names:?}"
        );
        // The active log must not be duplicated under logs/.
        assert!(
            !names.contains(&"logs/mbrc-core.log".to_owned()),
            "{names:?}"
        );

        // The window is only what was logged after the capture began.
        assert_eq!(read_entry(&path, "capture.log"), "after the capture\n");

        // And the report survives the round trip as parseable JSON.
        let report: Value =
            serde_json::from_str(&read_entry(&path, "report.json")).expect("report.json parses");
        assert_eq!(report["versions"]["core"], "1.5.0");

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_rotation_mid_capture_falls_back_to_the_whole_file_with_a_note() {
        let dir = seed("rotated", "a fresh, short log\n");
        let out = dir.join("out");
        let path = write(Inputs {
            storage: dir.to_str().expect("utf8 dir"),
            destination_dir: out.to_str().expect("utf8 dir"),
            report: Value::Null,
            // Far past the end: what a rotation looks like from here.
            log_offset: 10_000,
            started_unix_ms: 0,
        })
        .expect("bundle written");

        assert_eq!(read_entry(&path, "capture.log"), "a fresh, short log\n");
        assert!(entries(&path).contains(&"capture.log.note.txt".to_owned()));

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn rolled_logs_from_before_the_capture_are_left_out() {
        // The user's back catalogue is not part of the reproduction, and it is
        // the part of the bundle they cannot review before sending it.
        let dir = seed(
            "stale-rolls",
            "the whole log
",
        );
        let out = dir.join("out");
        let path = write(Inputs {
            storage: dir.to_str().expect("utf8 dir"),
            destination_dir: out.to_str().expect("utf8 dir"),
            report: Value::Null,
            log_offset: 0,
            // The capture began well after the seeded files were written.
            started_unix_ms: 4_102_444_800_000,
        })
        .expect("bundle written");

        let names = entries(&path);
        assert!(
            !names.iter().any(|name| name.starts_with("logs/")),
            "stale logs should not travel: {names:?}"
        );
        // The window itself still does.
        assert_eq!(
            read_entry(&path, "capture.log"),
            "the whole log
"
        );

        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_missing_log_still_produces_a_bundle() {
        // A capture on a fresh install, before anything was logged.
        let dir = std::env::temp_dir().join("mbrc-bundle-nolog");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("create scratch dir");
        let out = dir.join("out");
        let path = write(Inputs {
            storage: dir.to_str().expect("utf8 dir"),
            destination_dir: out.to_str().expect("utf8 dir"),
            report: serde_json::json!({}),
            log_offset: 0,
            started_unix_ms: 0,
        })
        .expect("bundle written");

        assert_eq!(read_entry(&path, "capture.log"), "");
        let _ = std::fs::remove_dir_all(&dir);
    }
}
