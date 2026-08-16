//! The TCP command server: a dedicated thread runs a Tokio runtime that accepts
//! connections and (Slice 3) fans out broadcasts. The pure handshake/dispatch
//! logic lives in [`session`]; the per-connection IO in [`connection`].

pub mod blocked;
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

/// How long after networking starts the session's one update check runs. Long
/// enough that the library reconcile and cover build have the machine to
/// themselves first; short enough that a user who opens the panel to look finds
/// the answer already there.
const STARTUP_UPDATE_CHECK_DELAY: std::time::Duration = std::time::Duration::from_secs(60);

/// How long the accept loop pauses after a failed `accept`, so a persistent
/// failure cannot become a busy loop.
const ACCEPT_ERROR_BACKOFF: std::time::Duration = std::time::Duration::from_millis(200);

/// How long networking shutdown waits for the mDNS advertisement to withdraw.
/// Long enough for the goodbye packets to leave, short enough that a wedged
/// daemon cannot hold up MusicBee closing.
const MDNS_SHUTDOWN_GRACE: std::time::Duration = std::time::Duration::from_secs(2);

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
        // A bad address in core_settings.json falls back to listening on every
        // interface rather than refusing to start, since a plugin that silently
        // never listens is the worst of the available failures.
        let bind_ip: std::net::IpAddr = match core.config.bind_address.parse() {
            Ok(ip) => ip,
            Err(_) => {
                tracing::warn!(
                    bind_address = %core.config.bind_address,
                    "invalid bind_address; falling back to 0.0.0.0"
                );
                std::net::IpAddr::from([0, 0, 0, 0])
            }
        };

        let listener = match TcpListener::bind((bind_ip, core.config.port)).await {
            Ok(listener) => {
                let _ = ready.send(Ok(()));
                listener
            }
            Err(e) => {
                let _ = ready.send(Err(e));
                return;
            }
        };
        tracing::info!(
            %bind_ip,
            port = core.config.port,
            "command server listening"
        );

        // Discovery advertises this host to the LAN over UDP multicast, which
        // makes no sense for a loopback-only listener that nothing off-box can
        // reach anyway. Skipping it also keeps it from binding INADDR_ANY and
        // raising the Windows Firewall prompt during tests.
        let discovery = if bind_ip.is_loopback() {
            tracing::debug!("discovery responder skipped (bound to loopback)");
            None
        } else {
            Some(tokio::spawn(crate::discovery::run(
                core.config.port,
                shutdown.clone(),
            )))
        };
        // The standard way to be found, additive to the responder above (#160).
        // Same loopback rule and for the same two reasons: nothing off-box can
        // reach a loopback listener, and binding 5353 on a test run would raise
        // the firewall prompt this suite exists without.
        let mdns = if bind_ip.is_loopback() || !core.config.mdns_enabled {
            tracing::debug!(
                enabled = core.config.mdns_enabled,
                "mDNS advertisement skipped"
            );
            None
        } else {
            Some(tokio::spawn(crate::mdns::run(
                core.config.port,
                shutdown.clone(),
            )))
        };
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

        // One update check per session, and only if the user asked for them.
        // Delayed so it does not compete with the library reconcile and cover
        // build above, which are what MusicBee was actually opened for; the
        // interval in the settings still decides whether the check does anything
        // once it fires. Skipped outright when the preference is off, so a staged
        // update stays the panel's headline instead of being overwritten with
        // "checking is disabled".
        if core.config.update_check_enabled {
            let update_core = core.clone();
            tokio::spawn(async move {
                tokio::time::sleep(STARTUP_UPDATE_CHECK_DELAY).await;
                crate::updates::service::start_check(update_core, false);
            });
        }

        tokio::select! {
            _ = accept_loop(listener, core.clone()) => {}
            _ = shutdown.notified() => tracing::info!("networking shutdown requested"),
        }
        if let Some(discovery) = discovery {
            discovery.abort();
        }
        // Awaited rather than aborted, unlike everything else here: it has a
        // shutdown path - the goodbye packets that get this host out of every
        // browser on the LAN - and the whole point of sending them is that they
        // arrive. Bounded, because a task that will not finish must not be able
        // to hold MusicBee's shutdown.
        if let Some(mdns) = mdns {
            if tokio::time::timeout(MDNS_SHUTDOWN_GRACE, mdns)
                .await
                .is_err()
            {
                tracing::warn!("mDNS did not withdraw in time");
            }
        }
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
    let Some(reconcile) = core.begin_reconcile() else {
        tracing::debug!("library reconcile already in progress; skipping");
        return;
    };

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
    // Before the finish notification, so a panel refreshing on that event sees
    // a cache that is no longer building. (The previous explicit
    // `end_reconcile()` ran after `notify(false)`, so the line could stay on
    // "Rebuilding cache..." until the next unrelated refresh.)
    drop(reconcile);
    notify(false);
}

/// Incremental album-cover refresh, run from the Scanner's nudge path (a
/// `FileAddedToLibrary` / `TagsChanged` / `FileDeleted` notification). Unlike
/// [`run_reconcile`] this does no metadata work and stays quiet: it only
/// broadcasts `librarycovercachebuildstatus` when the cached cover set actually
/// changed, so a nudge that touched no artwork produces no client traffic and no
/// status-bar churn.
///
/// The delta is entirely `warm_up` + `build`:
/// - `warm_up` re-maps album keys from the live `album_identifiers` and drops the
///   covers of albums that were modified (artwork edited) or removed (last track
///   deleted). An album key survives while any track keeps it in the library, and
///   `prune_orphans` only deletes a content-hashed file once no key references it,
///   so a delete never removes a cover another album still uses.
/// - `build` refetches the dropped/new albums' artwork.
///
/// The caller must already hold the reconcile single-flight guard (the Scanner
/// does), so this can't race an init/library-switch/manual rebuild.
pub(crate) fn refresh_covers_delta(core: &Arc<Core>) {
    use crate::cover::{cover_identifier, from_base64, store::AlbumIdentity};

    let identities: Vec<AlbumIdentity> = match core.providers.album_identifiers() {
        Ok(identifiers) => identifiers
            .into_iter()
            .map(|a| AlbumIdentity {
                key: cover_identifier(&a.artist, &a.album),
                path: a.path,
                modified: a.modified,
            })
            .collect(),
        Err(e) => {
            tracing::warn!(error = %e, "cover delta: album enumeration failed");
            return;
        }
    };

    // `dropped` (covers invalidated by an edit/delete) plus `stored` (covers
    // (re)fetched) together tell us whether the grid changed. A pure deletion
    // drops without storing; a new album with art stores without dropping.
    let before = core.cover_store.cached_count();
    core.cover_store.warm_up(&identities);
    let kept = core.cover_store.cached_count();
    let dropped = before.saturating_sub(kept);

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

    let changed = dropped > 0 || stats.stored > 0;
    tracing::debug!(
        albums = identities.len(),
        dropped,
        stored = stats.stored,
        cached = core.cover_store.cached_count(),
        changed,
        "cover delta complete"
    );
    if changed {
        // A single `false` frame means "cover cache updated, refetch the grid" -
        // the same signal the init/switch build sends on completion.
        core.broadcaster.broadcast(&[notifications::frame(
            "librarycovercachebuildstatus",
            serde_json::json!(false),
        )]);
    }
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
                    core.blocked.record(
                        peer.ip(),
                        peer.port(),
                        blocked::BlockReason::AddressNotAllowed,
                    );
                    tokio::spawn(reject_client(stream));
                    continue;
                }
                // Per-IP connection cap (loopback exempt). Reserve the slot here
                // so it pairs with the release after the connection ends.
                if !core.registry.try_admit_ip(peer.ip()) {
                    tracing::debug!(%peer, "rejecting client: per-IP connection cap reached");
                    core.blocked
                        .record(peer.ip(), peer.port(), blocked::BlockReason::PerIpCap);
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
            // Do not spin. A transient failure is worth retrying immediately in
            // principle, but a persistent one - descriptor exhaustion, a wedged
            // listener - would otherwise turn this into a busy loop pegging a core
            // inside MusicBee and filling the log as fast as it can rotate.
            Err(e) => {
                tracing::warn!(error = %e, "accept failed");
                tokio::time::sleep(ACCEPT_ERROR_BACKOFF).await;
            }
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

#[cfg(test)]
mod cover_delta_tests {
    use super::*;
    use crate::config::Config;
    use crate::protocol::messages::AlbumIdentifier;
    use crate::providers::MockProviders;
    use tokio::sync::mpsc;

    /// Build a `Core` on a fresh temp storage dir with a mock host that returns
    /// `albums` for the album scan and one canned JPEG for every artwork fetch.
    fn temp_core(name: &str, albums: Vec<AlbumIdentifier>) -> Arc<Core> {
        let dir = std::env::temp_dir().join(format!("mbrc-cover-delta-{name}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let mock = MockProviders {
            album_identifiers: albums,
            artwork_raw: crate::cover::to_base64(&crate::cover::test_jpeg_bytes(300, 300)),
            ..MockProviders::default()
        };
        let config = Config {
            storage_path: dir.to_string_lossy().into_owned(),
            ..Config::default()
        };
        Arc::new(Core::new(Arc::new(mock), config))
    }

    fn album(artist: &str, name: &str, path: &str, modified: i64) -> AlbumIdentifier {
        AlbumIdentifier {
            artist: artist.into(),
            album: name.into(),
            path: path.into(),
            modified,
        }
    }

    /// The nudge-path cover delta broadcasts `librarycovercachebuildstatus` only
    /// when the cached cover set actually changed: once for the initial build,
    /// then silence on a re-run that touched nothing (issue #91's requirement -
    /// no idle-nudge spam to connected clients).
    #[test]
    fn broadcasts_on_change_and_stays_silent_otherwise() {
        let core = temp_core("gate", vec![album("Artist", "Album", "/a.mp3", 0)]);
        let (tx, mut rx) = mpsc::unbounded_channel();
        core.broadcaster.register(1, tx);

        // First pass builds the album's cover -> grid changed -> one status frame.
        refresh_covers_delta(&core);
        let frame = rx.try_recv().expect("a built cover must broadcast");
        assert!(frame.contains("librarycovercachebuildstatus"), "{frame}");
        assert!(rx.try_recv().is_err(), "exactly one frame for one change");

        // Second pass: same unmodified album -> nothing dropped, nothing built.
        refresh_covers_delta(&core);
        assert!(
            rx.try_recv().is_err(),
            "an unchanged cover delta must not broadcast"
        );
    }
}
