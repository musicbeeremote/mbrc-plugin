//! The TCP command server: a dedicated thread runs a Tokio runtime that accepts
//! connections and (Slice 3) fans out broadcasts. The pure handshake/dispatch
//! logic lives in [`session`]; the per-connection IO in [`connection`].

pub mod broadcaster;
pub mod commands;
pub mod connection;
pub mod monitor;
pub mod notifications;
pub mod registry;
pub mod scanner;
pub mod session;

use std::net::IpAddr;
use std::sync::Arc;
use std::thread::JoinHandle;

use tokio::net::TcpListener;
use tokio::sync::Notify;

use crate::state::Core;

/// Handle to a running networking stack. Call [`NetHandle::stop`] to shut it
/// down and join the server thread.
pub struct NetHandle {
    shutdown: Arc<Notify>,
    thread: JoinHandle<()>,
}

impl NetHandle {
    /// Signal the server thread to stop and wait for it to finish.
    pub fn stop(self) {
        self.shutdown.notify_waiters();
        if self.thread.join().is_err() {
            tracing::warn!("networking thread panicked during shutdown");
        }
    }
}

/// Start the TCP command server (and the UDP discovery responder) on a
/// dedicated thread with its own Tokio runtime. Blocks only until the listener
/// is bound, so a bind failure (e.g. the port is in use) is reported
/// synchronously to the caller.
pub fn start(core: Arc<Core>) -> std::io::Result<NetHandle> {
    let shutdown = Arc::new(Notify::new());
    let shutdown_for_thread = shutdown.clone();
    let (ready_tx, ready_rx) = std::sync::mpsc::channel::<std::io::Result<()>>();

    let thread = std::thread::Builder::new()
        .name("mbrc-net".into())
        .spawn(move || run_thread(core, shutdown_for_thread, ready_tx))?;

    match ready_rx.recv() {
        Ok(Ok(())) => Ok(NetHandle { shutdown, thread }),
        Ok(Err(e)) => {
            let _ = thread.join();
            Err(e)
        }
        Err(_) => {
            let _ = thread.join();
            Err(std::io::Error::other(
                "networking thread exited before binding",
            ))
        }
    }
}

fn run_thread(
    core: Arc<Core>,
    shutdown: Arc<Notify>,
    ready: std::sync::mpsc::Sender<std::io::Result<()>>,
) {
    let runtime = match tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
    {
        Ok(rt) => rt,
        Err(e) => {
            let _ = ready.send(Err(e));
            return;
        }
    };

    runtime.block_on(async move {
        let listener = match TcpListener::bind(("0.0.0.0", core.config.port)).await {
            Ok(listener) => {
                let _ = ready.send(Ok(()));
                listener
            }
            Err(e) => {
                let _ = ready.send(Err(e));
                return;
            }
        };
        tracing::info!(port = core.config.port, "command server listening");

        let discovery = tokio::spawn(crate::discovery::run(core.config.port, shutdown.clone()));
        let monitor = tokio::spawn(monitor::run(core.clone(), shutdown.clone()));
        let scanner = tokio::spawn(scanner::run(core.clone(), shutdown.clone()));

        // Seed the now-playing cache once, off the async workers: the first
        // downloaded-lyrics fetch can block ~2.7s inside MusicBee, so paying it
        // here keeps the first client's `init` off that path.
        let seed_core = core.clone();
        tokio::task::spawn_blocking(move || seed_core.now_playing.refresh_all());

        // Reconcile the library in the background (one library scan): fingerprint
        // it to validate/refresh the metadata cache, eager-prewarm the browse
        // lists, then build the album cover cache (resize/hash/store), with the
        // build-status broadcast so clients refresh the cover grid. Off the async
        // workers - the scan, browse fetches, and artwork fetches are blocking
        // MusicBee calls.
        let cache_core = core.clone();
        tokio::task::spawn_blocking(move || reconcile_library(&cache_core));

        tokio::select! {
            _ = accept_loop(listener, core.clone()) => {}
            _ = shutdown.notified() => tracing::info!("networking shutdown requested"),
        }
        discovery.abort();
        monitor.abort();
        scanner.abort();
    });
}

/// Reconcile the library after a scan, then build the album cover cache. Shared
/// by init (background, on networking start) and a runtime `LibrarySwitched`.
///
/// What a reconcile pass refreshes. The `album_identifiers` scan (needed for
/// both the metadata fingerprint and the cover warm-up) always runs; the scope
/// selects which caches are then rebuilt.
#[derive(Clone, Copy, PartialEq, Eq)]
pub(crate) enum RebuildScope {
    /// Metadata browse lists only (cheap: a few library scans, no cover work).
    Metadata,
    /// Cover cache only (expensive: per-album artwork fetch + resize).
    Covers,
    /// Both - the init reconcile and a library switch.
    Both,
}

impl RebuildScope {
    fn does_metadata(self) -> bool {
        matches!(self, Self::Metadata | Self::Both)
    }
    fn does_covers(self) -> bool {
        matches!(self, Self::Covers | Self::Both)
    }
    fn status_label(self) -> &'static str {
        match self {
            Self::Metadata => "MusicBee Remote: Refreshing library metadata.",
            Self::Covers | Self::Both => "MusicBee Remote: Caching album covers.",
        }
    }
}

/// Full reconcile (metadata + covers) for init and a library switch. Mirrors C#
/// `CoverService.InitializeCacheAsync` plus the metadata cache.
pub(crate) fn reconcile_library(core: &Core) {
    run_reconcile(core, RebuildScope::Both);
}

/// On-demand rebuild with an explicit scope (the settings panel's per-cache
/// buttons, via `HostCommandType`). The caller forces a re-fetch by clearing the
/// relevant cache first (e.g. `metadata_cache.invalidate()` for a metadata
/// rebuild); this then re-fingerprints/re-prewarms and/or re-warms/re-builds.
pub(crate) fn rebuild(core: &Core, scope: RebuildScope) {
    run_reconcile(core, scope);
}

/// One `album_identifiers` scan feeds the metadata fingerprint and the cover
/// warm-up; the scope then selects which caches are rebuilt. Single-flight so
/// init, a library switch, and a manual rebuild can't run concurrently against
/// the shared caches. Skipped when no storage path is set (test `Config`s).
fn run_reconcile(core: &Core, scope: RebuildScope) {
    use crate::cover::{cover_identifier, from_base64, store::AlbumIdentity};

    if core.config.storage_path.is_empty() {
        return;
    }
    if !core.try_begin_reconcile() {
        tracing::debug!("library reconcile already in progress; skipping");
        return;
    }

    // Start/finish transitions. The host UI (settings cache line) always
    // refreshes; the `librarycovercachebuildstatus` broadcast to network clients
    // is cover-specific, so it only fires when covers are in scope.
    let notify = |building: bool| {
        core.providers
            .emit_event(crate::ffi::types::HostEventType::CacheStatusChanged, &[]);
        if scope.does_covers() {
            core.broadcaster.broadcast(&[notifications::frame(
                "librarycovercachebuildstatus",
                serde_json::json!(building),
            )]);
        }
    };

    // Surface progress in MusicBee's status bar (host-only UI); best-effort, so a
    // failed status update never aborts the build.
    let set_status = |message: String| {
        if let Err(e) = core.providers.set_background_task_message(&message) {
            tracing::debug!(error = %e, "reconcile: status message failed");
        }
    };

    notify(true);
    set_status(scope.status_label().to_string());

    let started = std::time::Instant::now();
    match core.providers.album_identifiers() {
        Ok(identifiers) => {
            let identities: Vec<AlbumIdentity> = identifiers
                .into_iter()
                .map(|a| AlbumIdentity {
                    // Identity lives in one place: the core hashes artist+album.
                    key: cover_identifier(&a.artist, &a.album),
                    path: a.path,
                    modified: a.modified,
                })
                .collect();
            let album_count = identities.len();

            if scope.does_metadata() {
                // Fingerprint the library and reconcile the metadata cache
                // (clears stale entries on a library change, validates for
                // reads/writes), then eager-prewarm the flat browse lists - but
                // only when the library changed or the persisted lists are
                // missing, so an unchanged warm cache skips the all-track tag read.
                let fingerprint = crate::metadata_cache::fingerprint(
                    identities.iter().map(|a| (a.key.as_str(), a.modified)),
                );
                let changed = core.metadata_cache.reconcile(fingerprint);
                let needs_rebuild =
                    changed || !commands::library::browse_lists_cached(&core.metadata_cache);
                let (counts, tracks) = if needs_rebuild {
                    // Small lists cached whole; the tracks list becomes the ordinal
                    // index (no full-tag read, no blob) - built last so its presence
                    // marks the whole cache warm for the next run.
                    let counts = commands::library::prewarm_browse_lists(
                        &core.metadata_cache,
                        core.providers.as_ref(),
                    );
                    let tracks = commands::library::build_track_index(
                        &core.metadata_cache,
                        core.providers.as_ref(),
                    );
                    (Some(counts), tracks)
                } else {
                    (None, core.metadata_cache.track_count() as usize)
                };
                tracing::info!(
                    albums = album_count,
                    fingerprint,
                    library_changed = changed,
                    rebuilt = needs_rebuild,
                    counts = ?counts,
                    tracks,
                    rss_mib = crate::logging::rss_mib(),
                    "library metadata reconciled"
                );
            }

            if scope.does_covers() {
                core.cover_store.warm_up(&identities);
                let prep_ms = started.elapsed().as_millis();
                tracing::info!(
                    albums = album_count,
                    prep_ms,
                    "cover cache: preparation complete"
                );

                let build_started = std::time::Instant::now();
                let providers = core.providers.clone();
                let stats = core.cover_store.build(
                    |path| {
                        let b64 = providers.artwork_raw(path).ok()?;
                        if b64.is_empty() {
                            return None;
                        }
                        from_base64(&b64)
                    },
                    core.config.log_level.is_trace(),
                );
                tracing::info!(
                    albums = album_count,
                    cached = core.cover_store.cached_count(),
                    attempted = stats.attempted,
                    stored = stats.stored,
                    no_art = stats.no_art,
                    failed = stats.failed,
                    fetch_ms = stats.fetch_ms,
                    store_ms = stats.store_ms,
                    slowest_ms = stats.slowest_ms,
                    slowest_path = %stats.slowest_path,
                    build_ms = build_started.elapsed().as_millis(),
                    total_ms = started.elapsed().as_millis(),
                    "cover cache build complete"
                );
            }
        }
        Err(e) => tracing::warn!(error = %e, "reconcile: album enumeration failed"),
    }

    set_status(if scope.does_covers() {
        format!(
            "MusicBee Remote: Done. {} album covers are now cached.",
            core.cover_store.cached_count()
        )
    } else {
        "MusicBee Remote: Done. Library metadata refreshed.".to_string()
    });
    notify(false);
    core.end_reconcile();
}

/// RAII release of a reserved per-IP connection slot. Dropping it (normal end
/// OR a panic unwinding the connection task) returns the slot to the registry,
/// so a panicking connection can't slowly exhaust an IP's cap.
struct IpSlotGuard {
    core: Arc<Core>,
    ip: IpAddr,
}

impl Drop for IpSlotGuard {
    fn drop(&mut self) {
        self.core.registry.release_ip(self.ip);
    }
}

async fn accept_loop(listener: TcpListener, core: Arc<Core>) {
    loop {
        match listener.accept().await {
            Ok((stream, peer)) => {
                let core = core.clone();
                // Client-address filtering (loopback always allowed). Rejected
                // peers get the `notallowed` frame then a close, matching the
                // shipped plugin - so the app shows "not allowed", not a silent drop.
                if !core.config.is_client_allowed(peer.ip()) {
                    tracing::debug!(%peer, "rejecting client: address not allowed");
                    tokio::spawn(reject_client(stream));
                    continue;
                }
                // Per-IP connection cap (loopback exempt). Reserve the slot here
                // so it pairs with the release after the connection ends.
                if !core.registry.try_admit_ip(peer.ip()) {
                    tracing::debug!(%peer, "rejecting client: per-IP connection cap reached");
                    tokio::spawn(reject_client(stream));
                    continue;
                }
                tokio::spawn(async move {
                    // Release the reserved per-IP slot on drop, so it is returned
                    // even if `connection::run` panics (unwinds) rather than
                    // returning - a leaked slot would eat into the per-IP cap.
                    let _slot = IpSlotGuard {
                        core: core.clone(),
                        ip: peer.ip(),
                    };
                    if let Err(e) = connection::run(stream, peer, core.clone()).await {
                        tracing::debug!(%peer, error = %e, "connection ended with error");
                    }
                });
            }
            Err(e) => tracing::warn!(error = %e, "accept failed"),
        }
    }
}

/// Send the `notallowed` frame to a filtered-out client and close, mirroring C#
/// `SocketServer.RejectConnection`.
async fn reject_client(mut stream: tokio::net::TcpStream) {
    use tokio::io::AsyncWriteExt;
    let frame = mbrc_wire::frame_line(&notifications::frame("notallowed", serde_json::json!("")));
    let _ = stream.write_all(frame.as_bytes()).await;
    let _ = stream.shutdown().await;
}
