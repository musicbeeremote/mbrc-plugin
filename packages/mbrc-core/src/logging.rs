//! Tracing setup and the `mbrc_log` / `mbrc_set_log_level` plumbing.
//!
//! Logs go to `<storage>/mbrc-core.log` (next to the C# `mbrc.log`), since there
//! is no console under MusicBee. `redact_frame` keeps base64 cover/lyrics blobs
//! out of the log. The filter is wrapped in a reload layer so `set_level` can
//! swap it live (the host drives it from the `log_level` setting).

use std::fs::{File, OpenOptions};
use std::io::{self, Write};
use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex, OnceLock};

use serde_json::Value;
use tracing_subscriber::fmt::writer::BoxMakeWriter;
use tracing_subscriber::prelude::*;
use tracing_subscriber::{fmt, reload, EnvFilter, Registry};

static INIT: OnceLock<()> = OnceLock::new();

/// Handle to the reloadable env-filter layer, so [`set_level`] can swap the
/// active filter at runtime. Set once by [`init`]; absent in unit tests (which
/// never call `init`), where `set_level` just validates.
static RELOAD_HANDLE: OnceLock<reload::Handle<EnvFilter, Registry>> = OnceLock::new();

/// The core's own log file, next to the C# `mbrc.log`, under the storage path.
const LOG_FILE: &str = "mbrc-core.log";
/// Roll the log when it reaches this size. Enforced at RUNTIME (every write),
/// not just at startup - a debug-level session on a big library writes GBs/hour,
/// so a startup-only guard let a single run grow the file unbounded.
const MAX_LOG_BYTES: u64 = 10 * 1024 * 1024;
/// How many rolled+gzipped generations to keep
/// (`mbrc-core.1.log.gz` .. `mbrc-core.N.log.gz`).
const KEEP_GENERATIONS: u32 = 3;
/// How many elements of a list body to keep when redacting a frame for a DEBUG
/// wire line; the omitted tail collapses to a `<+N more items…>` schema summary.
/// TRACE logs the full body (no cap).
pub const WIRE_LIST_SAMPLE: usize = 3;

/// A size-capped, self-rotating log sink. On each write it counts bytes and, once
/// the active file reaches [`MAX_LOG_BYTES`] (checked only at a line boundary so a
/// record is never split), rolls it: `mbrc-core.log` is renamed aside, gzipped to
/// `mbrc-core.1.log.gz` on a detached thread, older generations shift up, and a
/// fresh active file is opened. This is what actually bounds disk use; the
/// startup-only scheme before it did not.
struct RotatingWriter {
    inner: Mutex<Inner>,
    path: PathBuf,
    max_bytes: u64,
}

struct Inner {
    /// `None` only briefly during a roll, or if the file can't be reopened.
    file: Option<File>,
    written: u64,
}

impl RotatingWriter {
    fn open(path: PathBuf, max_bytes: u64) -> io::Result<Arc<Self>> {
        let file = OpenOptions::new().create(true).append(true).open(&path)?;
        // Seed the counter from the existing file so a resumed (already large)
        // log rolls promptly instead of growing past the cap this session.
        let written = file.metadata().map(|m| m.len()).unwrap_or(0);
        let w = Arc::new(Self {
            inner: Mutex::new(Inner {
                file: Some(file),
                written,
            }),
            path,
            max_bytes,
        });
        if written >= max_bytes {
            w.rotate(&mut w.inner.lock().unwrap());
        }
        Ok(w)
    }

    fn write_line(&self, buf: &[u8]) -> io::Result<usize> {
        let mut inner = self.inner.lock().unwrap();
        if let Some(file) = inner.file.as_mut() {
            file.write_all(buf)?;
            inner.written += buf.len() as u64;
            // Only roll at a newline boundary so a formatted record is never split
            // across two files.
            if inner.written >= self.max_bytes && buf.ends_with(b"\n") {
                self.rotate(&mut inner);
            }
        }
        Ok(buf.len())
    }

    fn rotate(&self, inner: &mut Inner) {
        // Close the active handle first: Windows won't rename a file that is open.
        if let Some(mut f) = inner.file.take() {
            let _ = f.flush();
        }
        let gz = |n: u32| self.path.with_file_name(format!("mbrc-core.{n}.log.gz"));
        let _ = std::fs::remove_file(gz(KEEP_GENERATIONS));
        for n in (1..KEEP_GENERATIONS).rev() {
            let _ = std::fs::rename(gz(n), gz(n + 1));
        }
        // Move the just-closed active file aside, then compress it off-thread so a
        // request's write path never blocks on gzip.
        let rolling = self.path.with_file_name("mbrc-core.rolling.log");
        let _ = std::fs::remove_file(&rolling);
        if std::fs::rename(&self.path, &rolling).is_ok() {
            let dst = gz(1);
            std::thread::spawn(move || {
                let _ = gzip_file(&rolling, &dst);
                let _ = std::fs::remove_file(&rolling);
            });
        }
        inner.file = OpenOptions::new()
            .create(true)
            .append(true)
            .open(&self.path)
            .ok();
        inner.written = 0;
    }
}

/// gzip `src` into `dst` (streamed, so a large rolled file never loads fully).
fn gzip_file(src: &Path, dst: &Path) -> io::Result<()> {
    let mut input = File::open(src)?;
    let out = File::create(dst)?;
    let mut enc = flate2::write::GzEncoder::new(out, flate2::Compression::default());
    io::copy(&mut input, &mut enc)?;
    enc.finish()?;
    Ok(())
}

/// Per-event writer handle handed to the tracing fmt layer. Cloned per event; all
/// clones share the one rotating file behind the `Arc`.
struct WriterHandle(Arc<RotatingWriter>);

impl Write for WriterHandle {
    fn write(&mut self, buf: &[u8]) -> io::Result<usize> {
        self.0.write_line(buf)
    }
    fn flush(&mut self) -> io::Result<()> {
        Ok(())
    }
}

/// Install the global tracing subscriber once, writing to `<storage>/mbrc-core.log`.
/// Under MusicBee there is no console, so a file sink is the only way to see
/// core logs. Falls back to stderr if the file can't be opened. Safe to call
/// repeatedly; only the first call takes effect.
pub fn init(storage_path: &str) {
    if INIT.get().is_some() {
        return;
    }
    // No console under MusicBee, so RUST_LOG is rarely set - the fallback is what
    // actually applies until the host pushes the `log_level` setting via
    // `mbrc_set_log_level`. Debug builds start a bit chattier; release starts at
    // info. `mbrc_core` covers module-path targets; `mbrc` covers the
    // `mbrc::wire` frame target.
    let fallback = if cfg!(debug_assertions) {
        "info,mbrc_core=debug,mbrc=debug"
    } else {
        "info"
    };
    let filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new(fallback));

    // Wrap the filter in a reload layer and stash the handle so `set_level` can
    // swap it live when the host changes the log level.
    let (filter_layer, handle) = reload::Layer::new(filter);
    let _ = RELOAD_HANDLE.set(handle);

    // The storage dir may not exist yet at first init; create it so the log
    // file open below doesn't silently fall back to stderr.
    let _ = std::fs::create_dir_all(storage_path);
    let path = Path::new(storage_path).join(LOG_FILE);
    // Runtime-rotating, size-capped file sink (bounds disk use). Fall back to
    // stderr only if the file can't be opened at all.
    let writer = match RotatingWriter::open(path, MAX_LOG_BYTES) {
        Ok(rot) => BoxMakeWriter::new(move || WriterHandle(rot.clone())),
        Err(_) => BoxMakeWriter::new(std::io::stderr),
    };
    let _ = tracing_subscriber::registry()
        .with(filter_layer)
        .with(fmt::layer().with_ansi(false).with_writer(writer))
        .try_init();

    let _ = INIT.set(());
}

/// Render a wire frame for logging. Redaction is **key-aware**: values under
/// known blob fields (cover art, image data, lyrics) become `<base64 N bytes>`
/// regardless of length, while every other field - notably file `path`s - stays
/// readable. A generous length cap is still applied to non-blob strings as a
/// safety net, but with a neutral `<N chars>` label rather than being
/// mislabeled as base64. Non-JSON input is truncated.
///
/// When `max_array` is `Some(n)`, long list bodies collapse to the first `n`
/// elements plus a `<+N more items; keys: …>` schema summary (the DEBUG wire
/// line); `None` keeps every element (the TRACE wire line).
pub fn redact_frame(frame: &str, max_array: Option<usize>) -> String {
    match serde_json::from_str::<Value>(frame) {
        Ok(mut v) => {
            redact_value(&mut v, None, max_array);
            v.to_string()
        }
        Err(_) => {
            let mut s: String = frame.chars().take(200).collect();
            if frame.len() > s.len() {
                s.push_str("...");
            }
            s
        }
    }
}

/// Field names whose values are always opaque blobs (base64 cover art, image
/// data, lyrics). Their values are elided regardless of length; every other
/// field is left readable so wire logs stay useful.
const BLOB_KEYS: &[&str] = &["cover", "image", "art", "lyrics"];

/// Safety cap for non-blob string values. Well above any realistic filesystem
/// path (Windows `MAX_PATH` is 260), so real `path` fields stay fully readable;
/// only a pathologically long non-blob string is shortened, and then with a
/// neutral `<N chars>` label - never mislabeled as base64.
const MAX_STR: usize = 512;

/// Whether `key` names a known blob field (case-insensitive).
fn is_blob_key(key: Option<&str>) -> bool {
    key.is_some_and(|k| BLOB_KEYS.iter().any(|b| k.eq_ignore_ascii_case(b)))
}

/// Redact a value for logging. `key` is the object field name the value sits
/// under (`None` at the frame root / inside arrays): blob fields are always
/// elided as `<base64 N bytes>`, other over-long strings get a neutral
/// `<N chars>` label. When `max_array` is set, long arrays are capped. Recurses
/// into arrays/objects.
fn redact_value(v: &mut Value, key: Option<&str>, max_array: Option<usize>) {
    match v {
        Value::String(s) => {
            if is_blob_key(key) {
                *v = Value::String(format!("<base64 {} bytes>", s.len()));
            } else if s.len() > MAX_STR {
                *v = Value::String(format!("<{} chars>", s.len()));
            }
        }
        Value::Array(items) => {
            if let Some(max) = max_array {
                if items.len() > max {
                    let extra = items.len() - max;
                    let schema = array_schema(&items[max]);
                    items.truncate(max);
                    for it in items.iter_mut() {
                        redact_value(it, key, max_array);
                    }
                    items.push(Value::String(format!("<+{extra} more items{schema}>")));
                    return;
                }
            }
            items
                .iter_mut()
                .for_each(|it| redact_value(it, key, max_array));
        }
        Value::Object(map) => map
            .iter_mut()
            .for_each(|(k, it)| redact_value(it, Some(k.as_str()), max_array)),
        _ => {}
    }
}

/// Compact shape hint for the elements dropped from a capped array: the object
/// keys (list items share a shape), or empty for scalars.
fn array_schema(sample: &Value) -> String {
    match sample {
        Value::Object(map) => {
            let keys: Vec<&str> = map.keys().map(String::as_str).collect();
            format!("; keys: {}", keys.join(","))
        }
        _ => String::new(),
    }
}

/// Current process resident set size (physical memory) in MiB, or `None` if the
/// platform query fails. The core runs as a 32-bit process, so this is the
/// number that proves the library cache stays O(page): during a full paging
/// sweep of a huge library it must stay flat, not track the library size. Cheap
/// but a syscall, so callers gate it on the log level before sampling.
pub fn rss_mib() -> Option<u64> {
    memory_stats::memory_stats().map(|s| (s.physical_mem / (1024 * 1024)) as u64)
}

/// Emit a log line forwarded from C#. `level`: 0=trace .. 4=error.
pub fn log(level: i32, target: &str, message: &str) {
    match level {
        0 => tracing::trace!("[{target}] {message}"),
        1 => tracing::debug!("[{target}] {message}"),
        2 => tracing::info!("[{target}] {message}"),
        3 => tracing::warn!("[{target}] {message}"),
        _ => tracing::error!("[{target}] {message}"),
    }
}

/// Swap the active log filter at runtime. Parses the directive (a bad one is
/// reported to C#), then reloads it through the handle installed by [`init`]. If
/// logging was never initialized (unit tests), it just validates.
pub fn set_level(directive: &str) -> Result<(), String> {
    let filter = EnvFilter::try_new(directive).map_err(|e| e.to_string())?;
    match RELOAD_HANDLE.get() {
        Some(handle) => handle.reload(filter).map_err(|e| e.to_string()),
        None => Ok(()),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn redact_elides_blob_fields() {
        let big: String = "A".repeat(500);
        let frame =
            format!(r#"{{"context":"nowplayingcover","data":{{"status":200,"cover":"{big}"}}}}"#);
        let out = redact_frame(&frame, None);
        assert!(out.contains("<base64 500 bytes>"), "got: {out}");
        assert!(out.contains(r#""status":200"#));
        // Short strings are left intact.
        let small = r#"{"context":"playershuffle","data":"autodj"}"#;
        assert_eq!(redact_frame(small, None), small);
    }

    #[test]
    fn redact_keeps_long_paths_readable() {
        // A realistic long filesystem path (well over the old 96-char cutoff)
        // must stay fully readable and must NOT be mislabeled as base64.
        let long_path = format!("/media/music/{}track.flac", "artist-album/".repeat(20));
        assert!(long_path.len() > 96 && long_path.len() < MAX_STR);
        let frame = format!(r#"{{"context":"nowplayingtrack","data":{{"path":"{long_path}"}}}}"#);
        let out = redact_frame(&frame, None);
        assert!(
            out.contains(&long_path),
            "path should stay intact, got: {out}"
        );
        assert!(!out.contains("base64"), "got: {out}");
    }

    #[test]
    fn redact_caps_huge_non_blob_strings_neutrally() {
        // A pathologically long non-blob string is still shortened as a safety
        // net, but with a neutral label rather than a base64 claim.
        let big = "x".repeat(1000);
        let frame = format!(r#"{{"context":"c","data":{{"note":"{big}"}}}}"#);
        let out = redact_frame(&frame, None);
        assert!(out.contains("<1000 chars>"), "got: {out}");
        assert!(!out.contains("base64"), "got: {out}");
    }

    #[test]
    fn redact_caps_list_bodies_with_schema_summary() {
        let items: Vec<String> = (0..10)
            .map(|i| format!(r#"{{"path":"p{i}","title":"t{i}"}}"#))
            .collect();
        let frame = format!(
            r#"{{"context":"nowplayinglist","data":{{"total":10,"data":[{}]}}}}"#,
            items.join(",")
        );
        // DEBUG path: keep WIRE_LIST_SAMPLE items + a schema summary of the rest.
        let capped = redact_frame(&frame, Some(WIRE_LIST_SAMPLE));
        assert!(capped.contains(r#""title":"t0""#), "got: {capped}");
        assert!(capped.contains(r#""title":"t2""#), "got: {capped}");
        assert!(!capped.contains(r#""title":"t3""#), "got: {capped}");
        assert!(
            capped.contains("<+7 more items; keys: path,title>"),
            "got: {capped}"
        );
        // TRACE path (None): every element retained.
        let full = redact_frame(&frame, None);
        assert!(full.contains(r#""title":"t9""#), "got: {full}");
        assert!(!full.contains("more items"), "got: {full}");
    }

    #[test]
    fn rss_mib_reports_a_plausible_value() {
        // On any real platform the running test process has resident memory, so
        // the query succeeds and returns a non-zero MiB figure.
        let rss = rss_mib().expect("process RSS should be queryable");
        assert!(rss > 0, "RSS should be non-zero, got {rss} MiB");
    }

    #[test]
    fn set_level_accepts_valid_and_rejects_invalid() {
        assert!(set_level("info").is_ok());
        assert!(set_level("mbrc_core=debug,info").is_ok());
        // A target with an unparseable level is rejected.
        assert!(set_level("mbrc_core=notalevel").is_err());
    }
}
