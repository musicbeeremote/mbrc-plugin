//! Transparent TCP tee proxy, embedded in the debugger.
//!
//! Point the client at `options.listen` instead of the real plugin, and every
//! CRLF-terminated JSON frame in either direction is forwarded byte-for-byte to
//! the upstream plugin while a copy is surfaced to the UI and (optionally)
//! appended to a JSONL capture file.
//!
//! # Capture format - `mbrc-capture/2`
//!
//! One JSON object per line. Two record kinds, discriminated by `type`:
//!
//! * **`frame`** - a wire frame. Carries the *true* TCP `conn_id` (assigned at
//!   accept, not inferred from handshake frames), a global monotonic `seq`, the
//!   exact `raw` bytes (source of truth for byte-faithful replay) plus a derived
//!   `frame` parse when the bytes were valid JSON, and - for `s2c` frames - a
//!   `reply_to` hint (the seq of the most recent `c2s` on the connection) that
//!   separates responses from unsolicited broadcasts.
//! * **`meta`** - capture/connection lifecycle: `capture-start` (format header),
//!   `open`, `handshake` (client type + negotiated protocol), and `close`
//!   (reason + which side hung up first).
//!
//! Meta lines have no `seq`/`dir`, so a consumer that only wants frames can skip
//! them with a single field check - the format stays additive.
//!
//! ```json
//! {"type":"meta","event":"capture-start","format":"mbrc-capture/2","listen":"0.0.0.0:3100","upstream":"127.0.0.1:3000","ts":"…"}
//! {"type":"frame","conn_id":0,"seq":0,"ts":"…","dir":"c2s","elapsed_ms":11,"raw":"{\"context\":\"player\",\"data\":\"Android\"}","frame":{"context":"player","data":"Android"}}
//! {"type":"meta","conn_id":0,"event":"handshake","client_type":"Android","protocol_version":4,"ts":"…"}
//! {"type":"meta","conn_id":0,"event":"close","ts":"…","reason":"eof","by":"client"}
//! ```

use std::path::PathBuf;
use std::sync::atomic::{AtomicU32, AtomicU64, Ordering};
use std::sync::{Arc, Mutex};
use std::time::Instant;

use mbrc_capture::{meta_capture_start, meta_close, meta_handshake, meta_open, Frame};
use serde::{Deserialize, Serialize};
use tauri::{AppHandle, Emitter, State};
use tokio::fs::{File, OpenOptions};
use tokio::io::{AsyncBufReadExt, AsyncRead, AsyncWrite, AsyncWriteExt, BufReader};
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::{watch, Mutex as AsyncMutex};
use tokio::task::JoinHandle;

/// Event channel names shared with the frontend.
pub const EVENT_PROXY: &str = "mbrc://proxy";
pub const EVENT_PROXY_STATE: &str = "mbrc://proxy-state";

/// Sentinel for "no c2s seen yet on this connection" in `Session::last_c2s`.
const NO_SEQ: u64 = u64::MAX;

#[derive(Debug, Deserialize)]
pub struct ProxyOptions {
    /// Address to listen on; point the client at this. e.g. "0.0.0.0:3100".
    #[serde(default = "default_listen")]
    pub listen: String,
    /// Upstream plugin address to forward to. e.g. "127.0.0.1:3000".
    #[serde(default = "default_upstream")]
    pub upstream: String,
    /// Optional JSONL capture file. Truncated on start so each session is a
    /// self-contained trace. When absent, frames are only surfaced to the UI.
    #[serde(default)]
    pub output: Option<PathBuf>,
}

fn default_listen() -> String {
    "0.0.0.0:3100".to_string()
}
fn default_upstream() -> String {
    "127.0.0.1:3000".to_string()
}

/// What the UI receives: a frame plus the client `peer` (not written to disk -
/// the on-disk record stays a pristine golden frame).
#[derive(Debug, Clone, Serialize)]
pub struct ProxyEvent {
    peer: String,
    #[serde(flatten)]
    record: Frame,
}

/// A client connection opening or closing, so the UI can track the live set.
#[derive(Debug, Clone, Serialize)]
pub struct ConnChange {
    pub id: u32,
    /// true = connected, false = disconnected.
    pub open: bool,
}

/// Lifecycle / status notices for the proxy, surfaced to the UI.
///
/// Two kinds, distinguished by `conn`:
/// * **server-state** (`conn` = None) - drives the listening flag: `start`
///   (listening true), `stop` (listening false), and non-fatal notices
///   (accept/capture errors, still listening).
/// * **connection** (`conn` = Some) - a client connect/disconnect; updates the
///   active-connection set *without* touching the listening flag, so a late
///   disconnect emitted while the proxy is stopping can't flip listening back on.
#[derive(Debug, Clone, Serialize)]
pub struct ProxyStateEvent {
    pub listening: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub detail: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub conn: Option<ConnChange>,
}

impl ProxyStateEvent {
    /// A server-state notice while listening (no connection delta).
    fn note(detail: impl Into<String>) -> Self {
        Self {
            listening: true,
            detail: Some(detail.into()),
            conn: None,
        }
    }

    /// A connection connect/disconnect delta.
    fn conn_change(id: u32, open: bool, detail: String) -> Self {
        Self {
            listening: true,
            detail: Some(detail),
            conn: Some(ConnChange { id, open }),
        }
    }
}

struct ProxyHandle {
    accept: JoinHandle<()>,
    shutdown: watch::Sender<bool>,
}

#[derive(Default)]
pub struct ProxyState {
    inner: Mutex<Option<ProxyHandle>>,
}

impl ProxyState {
    /// Install a new proxy, tearing down any previous one (signal shutdown so
    /// live sessions unwind, then abort the accept loop).
    fn replace(&self, handle: Option<ProxyHandle>) {
        let mut guard = self.inner.lock().unwrap();
        if let Some(old) = guard.take() {
            let _ = old.shutdown.send(true);
            old.accept.abort();
        }
        *guard = handle;
    }
}

/// Proxy-wide context, shared by every accepted connection. One capture file,
/// one global `seq`, one capture-start clock.
struct Shared {
    app: AppHandle,
    upstream: String,
    logfile: Option<Arc<AsyncMutex<File>>>,
    seq: AtomicU64,
    started: Instant,
    conn_counter: AtomicU32,
}

/// Per-connection context, shared by that connection's two direction tasks.
struct Session {
    shared: Arc<Shared>,
    conn_id: u32,
    peer: String,
    /// Seq of the last `c2s` frame; `NO_SEQ` until the first one arrives.
    last_c2s: AtomicU64,
    /// Client type parsed from the `player` handshake frame.
    client_type: Mutex<Option<String>>,
    /// Which side hung up first ("client"/"server"), set on the first EOF.
    closed_by: Mutex<Option<&'static str>>,
}

/// Serialize one record and append it as a line to the capture file, if one is
/// configured. A write failure is surfaced to the UI but never stalls the wire.
async fn append_line<S: Serialize>(shared: &Shared, value: &S) {
    let Some(logfile) = &shared.logfile else {
        return;
    };
    let Ok(mut line) = serde_json::to_vec(value) else {
        return;
    };
    line.push(b'\n');
    let mut f = logfile.lock().await;
    if let Err(e) = f.write_all(&line).await {
        let _ = shared.app.emit(
            EVENT_PROXY_STATE,
            ProxyStateEvent::note(format!("capture write failed: {e}")),
        );
    }
}

#[tauri::command]
pub async fn start_proxy(
    app: AppHandle,
    state: State<'_, ProxyState>,
    options: ProxyOptions,
) -> Result<(), String> {
    let listener = TcpListener::bind(&options.listen)
        .await
        .map_err(|e| format!("bind {} failed: {e}", options.listen))?;

    let logfile = match &options.output {
        Some(path) => {
            // Truncate, don't append: each capture owns a fresh `seq` sequence
            // starting at 0, so appending onto a prior trace would produce a file
            // with duplicate/rewound `seq` values - invalid as a golden fixture.
            let mut file = OpenOptions::new()
                .create(true)
                .write(true)
                .truncate(true)
                .open(path)
                .await
                .map_err(|e| format!("open {} failed: {e}", path.display()))?;
            // Format header, so a consumer can version-detect the capture.
            let header = meta_capture_start(&options.listen, &options.upstream);
            let mut line = serde_json::to_vec(&header).map_err(|e| e.to_string())?;
            line.push(b'\n');
            file.write_all(&line)
                .await
                .map_err(|e| format!("write header failed: {e}"))?;
            Some(Arc::new(AsyncMutex::new(file)))
        }
        None => None,
    };

    let shared = Arc::new(Shared {
        app: app.clone(),
        upstream: options.upstream.clone(),
        logfile,
        seq: AtomicU64::new(0),
        started: Instant::now(),
        conn_counter: AtomicU32::new(0),
    });

    let (shutdown_tx, shutdown_rx) = watch::channel(false);

    let accept = tokio::spawn(accept_loop(listener, shared, shutdown_rx));

    state.replace(Some(ProxyHandle {
        accept,
        shutdown: shutdown_tx,
    }));

    let _ = app.emit(
        EVENT_PROXY_STATE,
        ProxyStateEvent::note(format!(
            "listening on {} → {}",
            options.listen, options.upstream
        )),
    );
    Ok(())
}

#[tauri::command]
pub fn stop_proxy(app: AppHandle, state: State<'_, ProxyState>) {
    state.replace(None);
    let _ = app.emit(
        EVENT_PROXY_STATE,
        ProxyStateEvent {
            listening: false,
            detail: Some("stopped".into()),
            conn: None,
        },
    );
}

/// Accept connections until shutdown is signalled or the accept task is aborted.
async fn accept_loop(
    listener: TcpListener,
    shared: Arc<Shared>,
    mut shutdown: watch::Receiver<bool>,
) {
    loop {
        tokio::select! {
            _ = shutdown.changed() => {
                if *shutdown.borrow() {
                    return;
                }
            }
            accept = listener.accept() => {
                let (client, peer) = match accept {
                    Ok(c) => c,
                    Err(e) => {
                        let _ = shared.app.emit(
                            EVENT_PROXY_STATE,
                            ProxyStateEvent::note(format!("accept error: {e}")),
                        );
                        continue;
                    }
                };
                let conn_id = shared.conn_counter.fetch_add(1, Ordering::Relaxed);
                let _ = shared.app.emit(
                    EVENT_PROXY_STATE,
                    ProxyStateEvent::conn_change(conn_id, true, format!("client connected: {peer} (conn {conn_id})")),
                );
                let shared = Arc::clone(&shared);
                let shutdown = shutdown.clone();
                tokio::spawn(async move {
                    // `handle_session` emits its own close/error state on the way
                    // out (it knows which side hung up), so nothing to do here.
                    let _ = handle_session(client, peer.to_string(), conn_id, shared, shutdown).await;
                });
            }
        }
    }
}

async fn handle_session(
    client: TcpStream,
    peer: String,
    conn_id: u32,
    shared: Arc<Shared>,
    shutdown: watch::Receiver<bool>,
) -> std::io::Result<()> {
    let app = shared.app.clone();
    let upstream = TcpStream::connect(&shared.upstream).await?;

    // No Nagle: each JSON frame is its own message and latency matters for
    // trace fidelity.
    client.set_nodelay(true).ok();
    upstream.set_nodelay(true).ok();

    let session = Arc::new(Session {
        shared,
        conn_id,
        peer: peer.clone(),
        last_c2s: AtomicU64::new(NO_SEQ),
        client_type: Mutex::new(None),
        closed_by: Mutex::new(None),
    });

    // Connection-open lifecycle record.
    append_line(&session.shared, &meta_open(conn_id, &peer)).await;

    let (cr, cw) = client.into_split();
    let (ur, uw) = upstream.into_split();

    let s_c2s = Arc::clone(&session);
    let s_s2c = Arc::clone(&session);
    let sd_c2s = shutdown.clone();
    let sd_s2c = shutdown;

    let c2s =
        tokio::spawn(
            async move { pipe_frames(BufReader::new(cr), uw, "c2s", s_c2s, sd_c2s).await },
        );
    let s2c =
        tokio::spawn(
            async move { pipe_frames(BufReader::new(ur), cw, "s2c", s_s2c, sd_s2c).await },
        );

    // Either direction closing (or shutdown) ends the session.
    let result = tokio::try_join!(flatten(c2s), flatten(s2c));

    let by = session.closed_by.lock().unwrap().unwrap_or("proxy");
    let reason = match &result {
        Ok(_) => "eof".to_string(),
        Err(e) => format!("error:{e}"),
    };
    append_line(&session.shared, &meta_close(conn_id, &reason, by)).await;

    let detail = match &result {
        Ok(_) => format!("client disconnected: {peer} (conn {conn_id}, by {by})"),
        Err(e) => format!("session {peer} (conn {conn_id}) ended: {e}"),
    };
    let _ = app.emit(
        EVENT_PROXY_STATE,
        ProxyStateEvent::conn_change(conn_id, false, detail),
    );

    result.map(|_| ())
}

async fn flatten(h: JoinHandle<std::io::Result<()>>) -> std::io::Result<()> {
    match h.await {
        Ok(res) => res,
        Err(e) => Err(std::io::Error::other(e)),
    }
}

async fn pipe_frames<R, W>(
    mut src: BufReader<R>,
    mut dst: W,
    dir: &'static str,
    session: Arc<Session>,
    mut shutdown: watch::Receiver<bool>,
) -> std::io::Result<()>
where
    R: AsyncRead + Unpin,
    W: AsyncWrite + Unpin,
{
    let mut buf = Vec::with_capacity(4096);
    loop {
        buf.clear();
        let n = tokio::select! {
            _ = shutdown.changed() => {
                if *shutdown.borrow() {
                    dst.shutdown().await.ok();
                    return Ok(());
                }
                continue;
            }
            read = src.read_until(b'\n', &mut buf) => read?,
        };
        if n == 0 {
            // EOF - record which side hung up first, then half-close the
            // destination so the peer observes it too. Scope the guard so it is
            // released before the await below (a MutexGuard isn't Send).
            {
                let mut cb = session.closed_by.lock().unwrap();
                if cb.is_none() {
                    *cb = Some(if dir == "c2s" { "client" } else { "server" });
                }
            }
            dst.shutdown().await.ok();
            return Ok(());
        }

        // Forward the exact bytes first so a logging failure never stalls the
        // wire.
        dst.write_all(&buf).await?;

        let frame = trim_line_terminator(&buf);
        if frame.is_empty() {
            continue;
        }
        record_frame(&session, dir, frame).await;
    }
}

fn trim_line_terminator(buf: &[u8]) -> &[u8] {
    let mut end = buf.len();
    while end > 0 && matches!(buf[end - 1], b'\n' | b'\r') {
        end -= 1;
    }
    &buf[..end]
}

/// Build a frame record, emit it to the UI (with connection identity), append
/// it to the capture file, and maintain handshake/correlation state.
async fn record_frame(session: &Arc<Session>, dir: &str, frame_bytes: &[u8]) {
    let seq = session.shared.seq.fetch_add(1, Ordering::Relaxed);
    let elapsed_ms = session.shared.started.elapsed().as_millis();

    let mut record = Frame::new(session.conn_id, seq, dir, elapsed_ms, frame_bytes);

    if dir == "s2c" {
        let last = session.last_c2s.load(Ordering::Relaxed);
        if last != NO_SEQ {
            record.reply_to = Some(last);
        }
    } else {
        // c2s: advance the correlation cursor and sniff the handshake.
        session.last_c2s.store(seq, Ordering::Relaxed);
        maybe_handshake(session, &record).await;
    }

    // UI gets connection identity so it can group/filter; the file gets the bare
    // golden frame so each trace stays replay-ready.
    let _ = session.shared.app.emit(
        EVENT_PROXY,
        ProxyEvent {
            peer: session.peer.clone(),
            record: record.clone(),
        },
    );

    append_line(&session.shared, &record).await;
}

/// Sniff the two handshake frames (`player` → client type, `protocol` →
/// negotiated version) and, on the `protocol` frame, emit a `handshake` meta
/// record capturing what was negotiated on this connection.
async fn maybe_handshake(session: &Arc<Session>, record: &Frame) {
    match record.context() {
        Some("player") => {
            if let Some(ct) = record
                .frame
                .as_ref()
                .and_then(|f| f.get("data"))
                .and_then(|d| d.as_str())
            {
                *session.client_type.lock().unwrap() = Some(ct.to_string());
            }
        }
        Some("protocol") => {
            let protocol_version = record
                .frame
                .as_ref()
                .and_then(|f| f.get("data"))
                .and_then(|d| d.get("protocol_version"))
                .and_then(|v| v.as_i64());
            let client_type = session.client_type.lock().unwrap().clone();
            append_line(
                &session.shared,
                &meta_handshake(session.conn_id, client_type.as_deref(), protocol_version),
            )
            .await;
        }
        _ => {}
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn trims_crlf() {
        assert_eq!(trim_line_terminator(b"{}\r\n"), b"{}");
    }

    #[test]
    fn trims_bare_lf() {
        assert_eq!(trim_line_terminator(b"{}\n"), b"{}");
    }

    #[test]
    fn leaves_unterminated_alone() {
        assert_eq!(trim_line_terminator(b"{}"), b"{}");
    }

    #[test]
    fn empty_stays_empty() {
        assert_eq!(trim_line_terminator(b"\r\n"), b"");
    }
}
