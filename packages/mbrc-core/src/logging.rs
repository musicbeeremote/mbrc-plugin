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
use tracing_subscriber::{EnvFilter, Registry, fmt, reload};

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
            w.rotate(&mut w.lock());
        }
        Ok(w)
    }

    /// The writer state, surviving a poisoned lock.
    ///
    /// A panic anywhere while this is held would otherwise poison it, and every
    /// later log call - from every thread, including ones that only log because
    /// something already went wrong - would panic in turn. The data behind it is
    /// a file handle and a byte count; neither is left inconsistent by an
    /// unwind, so taking it back is strictly better than cascading.
    fn lock(&self) -> std::sync::MutexGuard<'_, Inner> {
        self.inner
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner())
    }

    fn write_line(&self, buf: &[u8]) -> io::Result<usize> {
        let mut inner = self.lock();
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
        let gz = |n: u32| self.path.with_file_name(rolled_log_name(n));
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

/// Filename of rolled generation `n`. Shared by the rotation itself and by the
/// diagnostics bundle, so the two can never disagree about what to look for.
pub(crate) fn rolled_log_name(n: u32) -> String {
    format!("mbrc-core.{n}.log.gz")
}

/// Path of the active core log inside `storage`.
pub(crate) fn active_log_path(storage: &str) -> PathBuf {
    Path::new(storage).join(LOG_FILE)
}

/// Every core log file present in `storage`, newest first: the active file, then
/// the gzipped generations that still exist. What the diagnostics bundle
/// collects; missing files are simply absent rather than an error, since a fresh
/// install has rolled nothing yet.
pub(crate) fn log_files(storage: &str) -> Vec<PathBuf> {
    let dir = Path::new(storage);
    let mut files = Vec::new();
    let active = dir.join(LOG_FILE);
    if active.is_file() {
        files.push(active);
    }
    for n in 1..=KEEP_GENERATIONS {
        let rolled = dir.join(rolled_log_name(n));
        if rolled.is_file() {
            files.push(rolled);
        }
    }
    files
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

/// Installs the global tracing subscriber once, writing to
/// `<storage>/mbrc-core.log`.
///
/// Under MusicBee there is no console, so a file sink is the only way to see
/// core logs. Falls back to stderr if the file can't be opened. Safe to call
/// repeatedly; only the first call takes effect.
pub fn init(storage_path: &str) {
    if INIT.get().is_some() {
        return;
    }
    // No console under MusicBee, so RUST_LOG is rarely set and this applies
    // until the host pushes `log_level`. `mbrc` is the wire frame target.
    let fallback = if cfg!(debug_assertions) {
        "info,mbrc_core=debug,mbrc=debug"
    } else {
        "info"
    };
    let filter = EnvFilter::try_from_default_env().unwrap_or_else(|_| EnvFilter::new(fallback));

    // Stashed so `set_level` can swap the filter live.
    let (filter_layer, handle) = reload::Layer::new(filter);
    let _ = RELOAD_HANDLE.set(handle);

    // Created first, or the log file open below falls back to stderr.
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

    install_panic_logger();
    let _ = INIT.set(());
}

/// Routes every panic, on any thread, into the log before the process moves on.
///
/// MusicBee gives a plugin no console, so a panic on a spawned thread writes to
/// a stderr nobody reads: the scanner or the cover build stops and the log says
/// nothing about why. The previous hook still runs afterwards, so a panic under
/// `cargo test` or the CLI keeps printing as it always did.
fn install_panic_logger() {
    let previous = std::panic::take_hook();
    std::panic::set_hook(Box::new(move |info| {
        tracing::error!(target: "mbrc::panic", "{}", describe_panic(info));
        previous(info);
    }));
}

/// Renders a panic as one line: which thread, where, and what it said.
fn describe_panic(info: &std::panic::PanicHookInfo<'_>) -> String {
    let thread = std::thread::current();
    let location = info
        .location()
        .map(|at| format!("{}:{}", at.file(), at.line()));
    panic_line(
        thread.name().unwrap_or("unnamed"),
        location.as_deref(),
        payload_message(info.payload()),
    )
}

/// The human-readable half of a panic payload.
///
/// `&str` for a bare `panic!`, `String` once it formats arguments, and neither
/// for `panic_any`, which is why the last arm exists at all.
fn payload_message(payload: &(dyn std::any::Any + Send)) -> &str {
    payload
        .downcast_ref::<&str>()
        .copied()
        .or_else(|| payload.downcast_ref::<String>().map(String::as_str))
        .unwrap_or("<non-string panic payload>")
}

/// Formats the one line a panic gets in the log.
fn panic_line(thread: &str, location: Option<&str>, message: &str) -> String {
    match location {
        Some(at) => format!("panicked on thread '{thread}' at {at}: {message}"),
        None => format!("panicked on thread '{thread}': {message}"),
    }
}

/// Renders a wire frame for logging.
///
/// Redaction is **key-aware**: blob fields (cover art, image data, lyrics) become
/// `<base64 N bytes>` at any length, while everything else - notably file
/// `path`s - stays readable under a generous `<N chars>` cap.
///
/// `max_array` of `Some(n)` collapses long list bodies to `n` elements plus a
/// `<+N more items; keys: …>` summary (DEBUG); `None` keeps them all (TRACE).
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

/// Pulls the `context` value out of an already-serialized wire frame without
/// re-parsing the whole thing.
///
/// Broadcast and ping frames reach the log as strings, and parsing the whole
/// thing just to name the event would double the work on every push. Returns
/// `""` when there is no readable `context`, which includes a value carrying an
/// escape sequence: [`mbrc_wire`]'s `frame` never emits one, so such a frame is
/// treated as unreadable rather than unescaped here.
pub fn frame_context(frame: &str) -> &str {
    context_of(frame).unwrap_or("")
}

fn context_of(frame: &str) -> Option<&str> {
    let key = frame.find("\"context\"")?;
    let rest = &frame[key + "\"context\"".len()..];
    let after = rest.trim_start().strip_prefix(':')?.trim_start();
    let body = after.strip_prefix('"')?;
    let end = body.find('"')?;
    let value = &body[..end];
    // An escape would need unescaping to be truthful; frames built by
    // `mbrc_wire::frame` never carry one, so treat it as unreadable instead.
    if value.contains('\\') {
        return None;
    }
    Some(value)
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

/// Redacts a value for logging. `key` is the object field name the value sits
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
            if let Some(max) = max_array
                && items.len() > max
            {
                let extra = items.len() - max;
                let schema = array_schema(&items[max]);
                items.truncate(max);
                for it in items.iter_mut() {
                    redact_value(it, key, max_array);
                }
                items.push(Value::String(format!("<+{extra} more items{schema}>")));
                return;
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

/// Compacts shape hint for the elements dropped from a capped array: the object
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
/// platform query fails.
///
/// The core runs as a 32-bit process, so this is the number that proves the
/// library cache stays O(page): during a full paging sweep of a huge library it
/// must stay flat, not track the library size. Cheap but a syscall, so callers
/// gate it on the log level before sampling.
pub fn rss_mib() -> Option<u64> {
    memory_stats::memory_stats().map(|s| (s.physical_mem / (1024 * 1024)) as u64)
}

/// Emits a log line forwarded from C#. `level`: 0=trace .. 4=error.
pub fn log(level: i32, target: &str, message: &str) {
    match level {
        0 => tracing::trace!("[{target}] {message}"),
        1 => tracing::debug!("[{target}] {message}"),
        2 => tracing::info!("[{target}] {message}"),
        3 => tracing::warn!("[{target}] {message}"),
        _ => tracing::error!("[{target}] {message}"),
    }
}

/// Swaps the active log filter at runtime.
///
/// Parses the directive (a bad one is reported to C#), then reloads it through
/// the handle installed by [`init`]. If logging was never initialized (unit
/// tests), it just validates.
pub fn set_level(directive: &str) -> Result<(), String> {
    let filter = EnvFilter::try_new(directive).map_err(|e| e.to_string())?;
    match RELOAD_HANDLE.get() {
        Some(handle) => handle.reload(filter).map_err(|e| e.to_string()),
        None => Ok(()),
    }
}

/// A capturing subscriber for tests that assert on emitted wire lines.
///
/// `tracing-subscriber` is a normal dependency of this crate, so a tiny layer
/// gives a real end-to-end assertion (the macro really fired, with these
/// fields) without pulling in a test-only tracing crate.
#[cfg(test)]
pub mod test_support {
    use std::fmt::Debug;
    use std::sync::{Arc, Mutex};

    use tracing::Subscriber;
    use tracing::field::{Field, Visit};
    use tracing_subscriber::Registry;
    use tracing_subscriber::layer::{Context, Layer};
    use tracing_subscriber::prelude::*;

    /// The subset of a `mbrc::wire` event that the broadcast/ping tests assert on.
    #[derive(Debug, Default, Clone)]
    pub struct WireLine {
        pub dir: String,
        pub kind: String,
        pub context: String,
        pub subscribers: Option<u64>,
        pub conn_id: Option<u64>,
        pub seq_present: bool,
        pub message: String,
    }

    impl Visit for WireLine {
        fn record_str(&mut self, field: &Field, value: &str) {
            match field.name() {
                "dir" => self.dir = value.to_string(),
                "kind" => self.kind = value.to_string(),
                "context" => self.context = value.to_string(),
                _ => {}
            }
        }

        fn record_u64(&mut self, field: &Field, value: u64) {
            match field.name() {
                "subscribers" => self.subscribers = Some(value),
                "conn_id" => self.conn_id = Some(value),
                "seq" => self.seq_present = true,
                _ => {}
            }
        }

        fn record_debug(&mut self, field: &Field, value: &dyn Debug) {
            let rendered = format!("{value:?}");
            // `&str` fields arrive through record_str; everything else lands
            // here, including the formatted message.
            match field.name() {
                "message" => self.message = rendered,
                "dir" | "kind" | "context" => {
                    self.record_str(field, rendered.trim_matches('"'));
                }
                _ => {}
            }
        }
    }

    #[derive(Default)]
    struct CaptureLayer {
        lines: Arc<Mutex<Vec<WireLine>>>,
    }

    impl<S: Subscriber> Layer<S> for CaptureLayer {
        fn on_event(&self, event: &tracing::Event<'_>, _ctx: Context<'_, S>) {
            if event.metadata().target() != "mbrc::wire" {
                return;
            }
            let mut line = WireLine::default();
            event.record(&mut line);
            self.lines.lock().unwrap().push(line);
        }
    }

    /// Runs `f` with a subscriber that captures every `mbrc::wire` event at DEBUG
    /// and above, and return what it captured.
    pub fn capture_wire_lines(f: impl FnOnce()) -> Vec<WireLine> {
        let lines = Arc::new(Mutex::new(Vec::new()));
        let layer = CaptureLayer {
            lines: Arc::clone(&lines),
        };
        let subscriber = Registry::default().with(layer.with_filter(
            tracing_subscriber::filter::LevelFilter::from_level(tracing::Level::DEBUG),
        ));
        tracing::subscriber::with_default(subscriber, f);
        let captured = lines.lock().unwrap();
        captured.clone()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_panic_payload_reads_as_its_message() {
        assert_eq!(payload_message(&"boom"), "boom");
        assert_eq!(payload_message(&String::from("formatted 1")), "formatted 1");
        assert_eq!(payload_message(&7u8), "<non-string panic payload>");
    }

    #[test]
    fn a_panic_line_names_the_thread_and_where_it_happened() {
        assert_eq!(
            panic_line("scanner", Some("src/server/scanner.rs:40"), "boom"),
            "panicked on thread 'scanner' at src/server/scanner.rs:40: boom"
        );
    }

    #[test]
    fn a_panic_without_a_location_still_names_the_thread() {
        assert_eq!(
            panic_line("unnamed", None, "boom"),
            "panicked on thread 'unnamed': boom"
        );
    }

    /// A panic on a spawned thread is the case the hook exists for: nothing
    /// joins these threads in production, so without it the failure is silent.
    #[test]
    fn a_panic_on_a_spawned_thread_is_described_with_its_location() {
        static SEEN: std::sync::Mutex<Vec<String>> = std::sync::Mutex::new(Vec::new());

        let previous = std::panic::take_hook();
        std::panic::set_hook(Box::new(|info| {
            SEEN.lock()
                .unwrap_or_else(std::sync::PoisonError::into_inner)
                .push(describe_panic(info));
        }));
        let worker = std::thread::Builder::new()
            .name("scanner".to_owned())
            .spawn(|| panic!("boom {}", 1))
            .expect("spawn worker");
        assert!(worker.join().is_err(), "the worker should have panicked");
        std::panic::set_hook(previous);

        let seen = SEEN
            .lock()
            .unwrap_or_else(std::sync::PoisonError::into_inner);
        let line = seen.last().expect("the hook ran");
        assert!(line.contains("thread 'scanner'"), "got: {line}");
        assert!(line.contains("boom 1"), "got: {line}");
        assert!(line.contains("logging.rs:"), "got: {line}");
    }

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

    #[test]
    fn frame_context_reads_real_frames() {
        assert_eq!(
            frame_context(r#"{"context":"playermute","data":true}"#),
            "playermute"
        );
        // Whitespace around the separator, and a context that isn't first.
        assert_eq!(
            frame_context(r#"{"data":1, "context" : "nowplayingposition"}"#),
            "nowplayingposition"
        );
        assert_eq!(frame_context(r#"{"context":"","data":""}"#), "");
    }

    #[test]
    fn frame_context_returns_empty_for_unreadable_input() {
        // Nothing that looks like a frame at all.
        assert_eq!(frame_context(""), "");
        assert_eq!(frame_context("not json"), "");
        assert_eq!(frame_context("{\"ctx\":\"playermute\"}"), "");
        // Truncated mid-frame: key present, value never closed.
        assert_eq!(frame_context("{\"context\":\"playerm"), "");
        assert_eq!(frame_context("{\"context\""), "");
        // A non-string value is not a context name.
        assert_eq!(frame_context(r#"{"context":42}"#), "");
        assert_eq!(frame_context(r#"{"context":null}"#), "");
        // Escapes would need unescaping to be truthful, so they read as unknown.
        assert_eq!(frame_context(r#"{"context":"a\"b","data":1}"#), "");
    }
}
