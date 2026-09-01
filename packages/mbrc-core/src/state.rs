//! Global core lifecycle: the initialized [`Core`] and its optional running
//! networking handle, behind one mutex.
//!
//! Unlike a `OnceLock`, this supports shutdown and re-initialization (MusicBee
//! can disable then re-enable the plugin): `initialize` sets it, `shutdown`
//! clears it, and a later `initialize` succeeds again.

use std::sync::atomic::{AtomicBool, AtomicU64, Ordering};
use std::sync::{Arc, Mutex, MutexGuard};

use tokio::sync::Notify;

use crate::config::Config;
use crate::cover::store::CoverStore;
use crate::ffi::types::{HostCommandType, HostQueryType, MbrcResult, NotificationType};
use crate::metadata_cache::MetadataCache;
use crate::nowplaying::NowPlayingCache;
use crate::providers::Providers;
use crate::server::blocked::BlockedLog;
use crate::server::broadcaster::Broadcaster;
use crate::server::registry::ConnectionRegistry;
use crate::server::{self, NetHandle, RebuildScope, notifications};
use crate::store::Db;

/// The initialized core: the provider boundary, config, the broadcast registry,
/// and the now-playing cache. Shared (via `Arc`) with the server thread and
/// notification handling.
pub struct Core {
    pub providers: Arc<dyn Providers>,
    pub config: Config,
    /// Fan-out to legacy V4/V5 broadcast subscribers (V4-shaped frames).
    pub broadcaster: Broadcaster,
    /// Fan-out to V6 broadcast subscribers (V6 event frames). Separate client set
    /// so V4-shaped frames never reach a V6 socket and vice-versa.
    pub v6_broadcaster: Broadcaster,
    pub now_playing: NowPlayingCache,
    /// The on-disk album cover cache (resize/hash/store/serve). Rooted at
    /// `config.storage_path`; the background build is kicked when networking
    /// starts (see `server::run_thread`).
    pub cover_store: Arc<CoverStore>,
    /// The library metadata cache (browse/navigation responses). Reconciled +
    /// eager-prewarmed by the same background task that builds the cover cache.
    pub metadata_cache: Arc<MetadataCache>,
    /// Single-flight guard for `reconcile_library` so the init reconcile and a
    /// `LibrarySwitched` (or two rapid switches) can't run concurrently.
    reconciling: AtomicBool,
    /// Bounds concurrent connections (per IP + per client_id) and supersedes a
    /// stale main socket on reconnect.
    pub registry: Arc<ConnectionRegistry>,
    /// Recent rejected connection attempts (address filter / caps), surfaced to
    /// the settings panel. In-memory ring buffer, not persisted.
    pub blocked: BlockedLog,
    conn_counter: AtomicU64,
    /// Wakes the background library Scanner to run a delta sooner. A library
    /// change notification (`FileAddedToLibrary`) is a debounced nudge on this,
    /// not a full cache clear (see `server::scanner`).
    pub scanner_nudge: Arc<Notify>,
    /// Set when the core is being torn down, and read by the long blocking work
    /// so it can stop between items.
    ///
    /// Without it, teardown waits for whatever is running: the cover build is a
    /// `spawn_blocking` task, and dropping a Tokio runtime waits for blocking
    /// tasks to finish. A first-run build of a large library is minutes of that,
    /// and MusicBee's own exit is what waits - closing MusicBee shortly after a
    /// fresh install would hang until every cover was cached.
    stopping: Arc<AtomicBool>,
}

impl Core {
    pub fn new(providers: Arc<dyn Providers>, config: Config) -> Self {
        let now_playing = NowPlayingCache::new(providers.clone());
        // One shared redb store for both durable caches.
        let db = Db::open(&config.storage_path);
        db.migrate_cover_state(&config.storage_path);
        let cover_store = Arc::new(CoverStore::new(db.clone(), config.storage_path.clone()));
        let metadata_cache = Arc::new(MetadataCache::new(db.clone()));
        let registry = Arc::new(ConnectionRegistry::new(
            config.max_conns_per_client,
            config.max_conns_per_ip,
        ));
        Self {
            providers,
            config,
            broadcaster: Broadcaster::default(),
            v6_broadcaster: Broadcaster::default(),
            now_playing,
            cover_store,
            metadata_cache,
            reconciling: AtomicBool::new(false),
            registry,
            blocked: BlockedLog::default(),
            conn_counter: AtomicU64::new(0),
            scanner_nudge: Arc::new(Notify::new()),
            stopping: Arc::new(AtomicBool::new(false)),
        }
    }

    /// Tells the long-running background work to stop at its next checkpoint.
    ///
    /// Set by both teardown paths, because the host uses both: `PluginHost` calls
    /// `mbrc_stop_networking` and only then `mbrc_shutdown`, so setting this in
    /// the latter alone means it is set after the wait it exists to prevent.
    pub fn begin_stopping(&self) {
        self.stopping.store(true, Ordering::Release);
    }

    /// Clears it again, because stopping networking is not always leaving.
    ///
    /// Saving a new port stops and restarts networking on the same core, and a
    /// flag left set there would silently disable the cover build for the rest
    /// of the session. Cleared as networking starts, which is also what kicks
    /// the reconcile that reads it.
    pub fn clear_stopping(&self) {
        self.stopping.store(false, Ordering::Release);
    }

    /// Whether teardown has started. Checked between items by work that would
    /// otherwise hold MusicBee's shutdown - see the `stopping` flag.
    pub fn is_stopping(&self) -> bool {
        self.stopping.load(Ordering::Acquire)
    }

    /// A fresh per-connection id (used as the broadcast-registry key).
    pub fn next_conn_id(&self) -> u64 {
        self.conn_counter.fetch_add(1, Ordering::Relaxed)
    }

    /// Acquires the single-flight reconcile right, or `None` if one is already in
    /// progress.
    ///
    /// A guard rather than a pair of calls: the work it spans is long, and the
    /// cost of not releasing it is silent and permanent - no rebuild would run
    /// again for the session, a library switch would be ignored, and the panel
    /// would sit on "Rebuilding cache..." forever. Dropping is the one thing
    /// that happens on every path out, including a panic.
    pub fn begin_reconcile(&self) -> Option<ReconcileGuard<'_>> {
        if self.reconciling.swap(true, Ordering::AcqRel) {
            None
        } else {
            Some(ReconcileGuard(self))
        }
    }

    /// Whether a library reconcile / cache build is currently running. Surfaced
    /// to the settings panel's cache-status line.
    pub fn is_reconciling(&self) -> bool {
        self.reconciling.load(Ordering::Acquire)
    }
}

/// Holds the single-flight reconcile right for as long as it is alive; see
/// [`Core::begin_reconcile`].
pub struct ReconcileGuard<'a>(&'a Core);

impl Drop for ReconcileGuard<'_> {
    fn drop(&mut self) {
        self.0.reconciling.store(false, Ordering::Release);
    }
}

struct Runtime {
    core: Arc<Core>,
    net: Option<NetHandle>,
}

static STATE: Mutex<Option<Runtime>> = Mutex::new(None);

fn lock() -> MutexGuard<'static, Option<Runtime>> {
    // A poisoned lock only means a prior panic; the state is still usable.
    STATE
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Stores the initialized core. `AlreadyInitialized` if called again without an
/// intervening `shutdown`.
pub fn initialize(providers: Arc<dyn Providers>, config: Config) -> MbrcResult {
    let mut guard = lock();
    if guard.is_some() {
        return MbrcResult::AlreadyInitialized;
    }
    *guard = Some(Runtime {
        core: Arc::new(Core::new(providers, config)),
        net: None,
    });
    MbrcResult::Ok
}

/// Serializes the initialized core's current settings as MessagePack (named
/// maps, the on-disk settable fields; `storage_path` is skipped).
///
/// `None` if not initialized. The settings panel reads this to populate its
/// controls - Rust owns the read. MessagePack (not JSON) so the host needs no
/// JSON dependency; the on-disk `core_settings.json` stays human-readable JSON
/// separately.
pub fn read_settings_bytes() -> Option<Vec<u8>> {
    let guard = lock();
    let config = &guard.as_ref()?.core.config;
    // Named maps so the C# contractless resolver reads by property name.
    rmp_serde::to_vec_named(config).ok()
}

/// Validates and persists new settings (MessagePack from the host) to
/// `core_settings.json` in the core's storage dir - Rust owns the write.
///
/// The file stays JSON on disk; only the transport is MessagePack, and the host
/// re-inits to apply rather than this hot-reloading. The write is a **merge**:
/// the panel knows only a subset of [`Config`], so deserializing into a fresh
/// one would reset every field it omits and wipe the Rust-only settings.
///
/// # Errors
/// The payload failed to parse, failed validation, or could not be written.
pub fn write_settings_bytes(bytes: &[u8]) -> Result<(), String> {
    let storage = storage_path().ok_or("core not initialized")?;
    let patch: serde_json::Value =
        rmp_serde::from_slice(bytes).map_err(|e| format!("invalid settings msgpack: {e}"))?;
    let mut config = merge_settings(Config::load(&storage), &patch)?;
    config.validate()?;
    config.storage_path = storage.clone();
    let pretty = serde_json::to_string_pretty(&config).map_err(|e| e.to_string())?;
    let path = std::path::Path::new(&storage).join("core_settings.json");
    std::fs::write(&path, pretty).map_err(|e| format!("write settings: {e}"))
}

/// Hands the staged update to the elevated helper.
///
/// The version the host reports is the one the staged bundle has to beat: a
/// signature proves a bundle is ours, not that it is newer than what is running,
/// and every release is public. `None` when the core is not initialized.
pub fn apply_staged_update() -> Option<crate::ffi::types::UpdateLaunch> {
    let core = {
        let guard = lock();
        guard.as_ref()?.core.clone()
    };
    let storage = core.config.storage_path.clone();
    if storage.is_empty() {
        return None;
    }
    // Safe to substitute: both are stamped from the same `Directory.Build.props`.
    let current = core.providers.plugin_version().unwrap_or_else(|e| {
        tracing::warn!(error = %e, "the host could not report its version; using the core's");
        crate::updates::CORE_VERSION.to_owned()
    });
    Some(crate::updates::elevate::launch(&storage, &current))
}

/// The initialized core, or `None` before init / after shutdown.
///
/// For background work that outlives the call that started it and needs the core
/// itself rather than just a yes/no - the capture watchdog restoring the log
/// level, for instance. Cloning the `Arc` out under the lock keeps the caller
/// from holding the global mutex while it works.
pub fn core_handle() -> Option<Arc<Core>> {
    lock().as_ref().map(|state| state.core.clone())
}

/// Whether a core is currently initialized.
///
/// For background work that outlives the call that started it: the C# callback
/// delegates are released at `mbrc_shutdown`, so a long-running job must not
/// reach back into the host after one. This narrows that window rather than
/// closing it - a shutdown between the check and the call is still possible -
/// but it takes a stalled download that finishes minutes after MusicBee exited
/// out of the picture entirely.
pub fn is_initialized() -> bool {
    lock().is_some()
}

/// The storage directory MusicBee handed the core at init, or `None` before
/// then. The one place that answer lives, so nothing has to be told it twice.
pub fn storage_path() -> Option<String> {
    lock()
        .as_ref()
        .map(|state| state.core.config.storage_path.clone())
}

/// Applies the host's settings payload over `base`, field by field.
///
/// Going through `serde_json::Value` keeps this free of a hand-maintained list
/// of settable fields: whatever keys the payload carries win, everything else
/// keeps its on-disk value. Keys the core does not know are dropped (`base` is
/// re-serialized from a typed `Config`, so a stale key like the pre-`log_level`
/// `debug` bool is not resurrected). The payload must be a map.
fn merge_settings(base: Config, patch: &serde_json::Value) -> Result<Config, String> {
    let patch = patch
        .as_object()
        .ok_or("invalid settings payload: expected a map")?;
    let mut merged = serde_json::to_value(&base).map_err(|e| e.to_string())?;
    let object = merged
        .as_object_mut()
        .expect("Config serializes to a JSON object");
    for (key, value) in patch {
        object.insert(key.clone(), value.clone());
    }
    serde_json::from_value(merged).map_err(|e| format!("invalid settings payload: {e}"))
}

/// Cache health surfaced to the settings panel. Field names are the MessagePack
/// keys the host reads by (contractless resolver), so they must match the C#
/// `CoreCacheStatus` DTO.
#[derive(serde::Serialize)]
struct CacheStatus {
    /// Tracks in the cached browse list (0 if never cached).
    tracks_cached: u32,
    /// Albums with a cached, resized cover.
    covers_cached: u32,
    /// A reconcile / cache build is currently running.
    building: bool,
    /// The metadata (browse) cache is validated and serving from redb.
    metadata_ready: bool,
}

/// Dispatches a host -> core query (request/response).
///
/// Returns the MessagePack result, or `None` when the core is not initialized
/// or the handler has no answer. The generic entry point for the C# host's
/// app-level reads; add a [`HostQueryType`] variant + arm here rather than a
/// new FFI export.
pub fn host_query(kind: HostQueryType, _params: &[u8]) -> Option<Vec<u8>> {
    match kind {
        HostQueryType::CacheStatus => cache_status_bytes(),
        HostQueryType::RecentBlocked => recent_blocked_bytes(),
        HostQueryType::ListeningAddresses => listening_info_bytes(),
        HostQueryType::UpdateStatus => update_status_bytes(),
        // Answers without the core, so a panel opened before init still renders
        // a Diagnostics group instead of a blank one.
        HostQueryType::CaptureStatus => crate::diagnostics::capture::status_bytes(),
    }
}

/// Dispatches a host -> core command (fire-and-forget). The generic entry point
/// for the C# host's app-level actions; add a [`HostCommandType`] variant + arm.
pub fn host_command(kind: HostCommandType, params: &[u8]) -> MbrcResult {
    match kind {
        HostCommandType::RebuildMetadata => rebuild(RebuildScope::Metadata),
        HostCommandType::RebuildCovers => rebuild(RebuildScope::Covers),
        HostCommandType::ClearBlockedLog => clear_blocked(),
        // Forced: the panel's button is an instruction, not a schedule, so it
        // ignores both the enabled preference and the interval.
        HostCommandType::CheckForUpdate => {
            with_core(|core| crate::updates::service::start_check(core, true))
        }
        HostCommandType::DownloadUpdate => with_core(crate::updates::service::start_download),
        HostCommandType::SkipUpdate => {
            with_core(|core| crate::updates::service::skip_available(&core))
        }
        HostCommandType::StartCapture => match capture_request(params) {
            Ok(request) => with_core(|core| crate::diagnostics::capture::start(&core, request)),
            Err(error) => {
                tracing::warn!(error = %error, "ignoring a malformed capture request");
                MbrcResult::InvalidArgument
            }
        },
        HostCommandType::StopCapture => match capture_request(params) {
            Ok(request) => with_core(|core| crate::diagnostics::capture::stop(core, &request)),
            Err(error) => {
                tracing::warn!(error = %error, "ignoring a malformed capture request");
                MbrcResult::InvalidArgument
            }
        },
        HostCommandType::CancelCapture => {
            with_core(|core| crate::diagnostics::capture::cancel(&core))
        }
    }
}

/// Decodes a capture command's params. An empty payload is a valid request with
/// nothing in it (`CancelCapture` sends one), so it is not an error.
fn capture_request(params: &[u8]) -> Result<crate::ffi::dtos::CaptureRequest, String> {
    if params.is_empty() {
        return Ok(crate::ffi::dtos::CaptureRequest::default());
    }
    rmp_serde::from_slice(params).map_err(|e| e.to_string())
}

/// Runs `action` against the initialized core, or report `NotInitialized`. The
/// lock is released before `action` runs: the update jobs it starts touch the
/// core from their own threads.
fn with_core(action: impl FnOnce(Arc<Core>) -> MbrcResult) -> MbrcResult {
    let core = {
        let guard = lock();
        match guard.as_ref() {
            Some(runtime) => runtime.core.clone(),
            None => return MbrcResult::NotInitialized,
        }
    };
    action(core)
}

/// Serializes the current cache status as MessagePack for the settings panel.
/// `None` if the core is not initialized.
fn cache_status_bytes() -> Option<Vec<u8>> {
    let core = {
        let guard = lock();
        guard.as_ref()?.core.clone()
    };
    let status = CacheStatus {
        tracks_cached: crate::server::commands::library::cached_tracks_count(&core.metadata_cache)
            as u32,
        covers_cached: core.cover_store.cached_count() as u32,
        building: core.is_reconciling(),
        metadata_ready: core.metadata_cache.is_validated(),
    };
    rmp_serde::to_vec_named(&status).ok()
}

/// Serializes the recent blocked-connection entries (newest first) as MessagePack
/// for the settings panel. `None` if the core is not initialized; an empty log
/// serializes to an empty array (not `None`).
fn recent_blocked_bytes() -> Option<Vec<u8>> {
    let core = {
        let guard = lock();
        guard.as_ref()?.core.clone()
    };
    rmp_serde::to_vec_named(&core.blocked.recent()).ok()
}

/// Serializes the addresses a client can reach the server on (candidate interface
/// IPv4s + the bound port) as MessagePack for the settings panel. `None` if the
/// core is not initialized; an interface-less host yields an empty address list.
fn listening_info_bytes() -> Option<Vec<u8>> {
    let port = {
        let guard = lock();
        guard.as_ref()?.core.config.port
    };
    let addresses = crate::discovery::usable_ipv4_ifaces()
        .into_iter()
        .map(|(ip, _mask)| ip.to_string())
        .collect();
    let info = crate::ffi::dtos::ListeningInfo { port, addresses };
    rmp_serde::to_vec_named(&info).ok()
}

/// Serializes where the update flow stands as MessagePack for the settings panel.
/// `None` if the core is not initialized; every other state is a real answer,
/// including "nothing has been checked yet".
fn update_status_bytes() -> Option<Vec<u8>> {
    let core = {
        let guard = lock();
        guard.as_ref()?.core.clone()
    };
    crate::updates::service::status_bytes(&core)
}

/// Clears the in-memory blocked-connection log (the panel's "Clear" button).
fn clear_blocked() -> MbrcResult {
    let guard = lock();
    match guard.as_ref() {
        Some(runtime) => {
            runtime.core.blocked.clear();
            MbrcResult::Ok
        }
        None => MbrcResult::NotInitialized,
    }
}

/// Kicks a background rebuild of the requested cache (the settings panel's per-
/// cache buttons). A metadata rebuild first invalidates the metadata cache so the
/// reconcile re-fetches the browse lists (an unchanged fingerprint would
/// otherwise skip the re-fetch); a cover rebuild is incremental (re-fetches
/// missing/changed art). Spawns the work on a plain thread - it does blocking FFI
/// and is single-flight guarded, so a rebuild while one runs is a harmless no-op.
fn rebuild(scope: RebuildScope) -> MbrcResult {
    let core = {
        let guard = lock();
        match guard.as_ref() {
            Some(runtime) => runtime.core.clone(),
            None => return MbrcResult::NotInitialized,
        }
    };
    if scope == RebuildScope::Metadata {
        core.metadata_cache.invalidate();
    }
    let rebuild_core = core.clone();
    std::thread::spawn(move || server::rebuild(&rebuild_core, scope));
    MbrcResult::Ok
}

/// Builds and fan out the broadcast frames for a MusicBee notification.
///
/// Library-changing notifications also maintain the metadata cache, which
/// happens here rather than in the pure [`notifications::on_notification`]
/// because it needs the owned `Arc<Core>`: the C# notification thread has no
/// Tokio runtime, so the reconcile is spawned on a plain thread and does
/// blocking FFI.
pub fn handle_notification(ntype: NotificationType) -> MbrcResult {
    // Clone the Arc and drop the lock before querying/broadcasting.
    let core = {
        let guard = lock();
        match guard.as_ref() {
            Some(runtime) => runtime.core.clone(),
            None => return MbrcResult::NotInitialized,
        }
    };

    dispatch_notification(&core, ntype);
    MbrcResult::Ok
}

/// Dispatches one notification against a specific `core`.
///
/// Split out of [`handle_notification`] so the cache maintenance and the V4/V6
/// fan-out are testable without the global runtime.
pub fn dispatch_notification(core: &Arc<Core>, ntype: NotificationType) {
    match ntype {
        NotificationType::LibrarySwitched => {
            // Gate reads off + clear immediately so nothing stale is served in
            // the gap; the reconcile re-fingerprints, re-prewarms, re-validates.
            core.metadata_cache.invalidate();
            broadcast_library_changed(core);
            let reconcile_core = core.clone();
            std::thread::spawn(move || server::reconcile_library(&reconcile_core));
            return;
        }
        NotificationType::FileAddedToLibrary => {
            // A nudge, not a clear: the Scanner debounces these, so a big import
            // collapses to a scan or two instead of a wipe per file.
            core.scanner_nudge.notify_one();
            broadcast_library_changed(core);
        }
        NotificationType::NowPlayingListChanged => core.now_playing.bump_list_version(),
        _ => {}
    }

    let (v4_frames, v6_frames) = notifications::on_notification(core, ntype);
    core.broadcaster.broadcast(&v4_frames);
    core.v6_broadcaster.broadcast(&v6_frames);
}

/// Tells V6 subscribers the server is going away, so a client can tell a
/// deliberate shutdown from a network drop and not reconnect into a closing
/// MusicBee.
///
/// Best-effort by nature: it is queued on each connection's writer just before
/// networking is torn down, so it goes out with whatever time the shutdown
/// sequence leaves. V4 has no frame for this and is left alone.
fn broadcast_server_shutdown(core: &Core) {
    core.v6_broadcaster.broadcast(&[mbrc_wire::v6::event(
        "server_shutdown",
        serde_json::json!({}),
    )]);
}

/// Fans out the V6 `library_changed` marker; the client re-queries what it needs.
///
/// V6-only: V4 has no equivalent broadcast, where a library change is cache
/// maintenance and nothing more.
fn broadcast_library_changed(core: &Core) {
    core.v6_broadcaster.broadcast(&[mbrc_wire::v6::event(
        "library_changed",
        serde_json::json!({}),
    )]);
}

/// Tells the background work to wind down, without stopping anything yet.
///
/// For a host that knows it is about to tear down but has its own work to do
/// first - an uninstall closes a window and takes a menu entry out before it
/// disposes anything. Those are milliseconds, and this hands them to the cover
/// build as notice, so by the time the join comes it has usually already
/// stopped. Idempotent, and safe to call when nothing is running.
pub fn begin_stopping() -> MbrcResult {
    match lock().as_ref() {
        Some(runtime) => {
            runtime.core.begin_stopping();
            MbrcResult::Ok
        }
        None => MbrcResult::NotInitialized,
    }
}

/// Stops networking (if running) and drops the core, allowing a later re-init.
pub fn shutdown() -> MbrcResult {
    let mut guard = lock();
    match guard.take() {
        Some(runtime) => {
            runtime.core.begin_stopping();
            if let Some(net) = runtime.net {
                broadcast_server_shutdown(&runtime.core);
                net.stop();
            }
            MbrcResult::Ok
        }
        None => MbrcResult::NotInitialized,
    }
}

/// Starts the TCP server + discovery responder.
pub fn start_networking() -> MbrcResult {
    let mut guard = lock();
    let Some(runtime) = guard.as_mut() else {
        return MbrcResult::NotInitialized;
    };
    if runtime.net.is_some() {
        return MbrcResult::AlreadyRunning;
    }
    // A previous stop (a port change, say) left this set; the work it gates is
    // started by the call below.
    runtime.core.clear_stopping();
    match server::start(runtime.core.clone()) {
        Ok(net) => {
            runtime.net = Some(net);
            MbrcResult::Ok
        }
        Err(e) => {
            tracing::error!(error = %e, "failed to start networking");
            MbrcResult::RuntimeError
        }
    }
}

/// Stops the TCP server + discovery responder (leaves the core initialized).
pub fn stop_networking() -> MbrcResult {
    let mut guard = lock();
    let Some(runtime) = guard.as_mut() else {
        return MbrcResult::NotInitialized;
    };
    match runtime.net.take() {
        Some(net) => {
            // Before the join inside `stop`: this is the call the host makes
            // first, so it is the one that has to release the long blocking work.
            runtime.core.begin_stopping();
            broadcast_server_shutdown(&runtime.core);
            net.stop();
            MbrcResult::Ok
        }
        None => MbrcResult::NotRunning,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::providers::NullProviders;

    #[test]
    fn settings_and_host_queries_round_trip_through_state() {
        let dir = std::env::temp_dir().join("mbrc-settings-state-test");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();

        let config = Config {
            port: 4321,
            storage_path: dir.to_string_lossy().into_owned(),
            ..Config::default()
        };

        let _ = shutdown(); // ensure a clean slate (this is the only STATE test)
        assert_eq!(initialize(Arc::new(NullProviders), config), MbrcResult::Ok);

        a_read_reflects_the_in_memory_config();
        a_valid_write_persists_as_json(&dir);
        an_invalid_port_is_refused();
        a_panel_payload_leaves_unknown_fields_alone(&dir);
        an_empty_blocked_log_queries_as_an_empty_array();
        listening_addresses_reports_the_port_the_core_is_bound_to();

        let _ = shutdown();
    }

    fn a_read_reflects_the_in_memory_config() {
        let bytes = read_settings_bytes().expect("read settings");
        let echoed: Config = rmp_serde::from_slice(&bytes).unwrap();
        assert_eq!(echoed.port, 4321);
        assert_eq!(
            echoed.storage_path, "",
            "storage_path is skipped, not exposed"
        );
    }

    /// MessagePack in, JSON on disk.
    fn a_valid_write_persists_as_json(dir: &std::path::Path) {
        let update = Config {
            port: 5555,
            filter_mode: crate::config::FilterMode::Specific,
            allowed_addresses: vec!["10.0.0.0/8".to_string()],
            ..Config::default()
        };
        write_settings_bytes(&rmp_serde::to_vec_named(&update).unwrap()).expect("write settings");
        let on_disk = std::fs::read_to_string(dir.join("core_settings.json")).unwrap();
        assert!(on_disk.contains("5555"));
        assert!(on_disk.contains("10.0.0.0/8"));
    }

    fn an_invalid_port_is_refused() {
        let bad = rmp_serde::to_vec_named(&Config {
            port: 0,
            ..Config::default()
        })
        .unwrap();
        assert!(write_settings_bytes(&bad).is_err());
    }

    /// The C# `CoreSettings` DTO carries 8 of [`Config`]'s fields, so saving from
    /// the panel must leave every field it does not know about alone.
    fn a_panel_payload_leaves_unknown_fields_alone(dir: &std::path::Path) {
        std::fs::write(
            dir.join("core_settings.json"),
            r#"{"port":3000,"bind_address":"127.0.0.1","tcp_keepalive_secs":99}"#,
        )
        .unwrap();
        let panel = serde_json::json!({
            "port": 6001,
            "filter_mode": "all",
            "base_ip": "",
            "last_octet_max": 254,
            "allowed_addresses": [],
            "search_source": 1,
            "update_firewall": false,
            "log_level": "debug",
        });
        write_settings_bytes(&rmp_serde::to_vec_named(&panel).unwrap()).expect("panel write");
        let saved: Config =
            serde_json::from_str(&std::fs::read_to_string(dir.join("core_settings.json")).unwrap())
                .unwrap();
        assert_eq!(saved.port, 6001, "the panel's own edit lands");
        assert_eq!(saved.log_level, crate::config::LogLevel::Debug);
        assert_eq!(saved.bind_address, "127.0.0.1", "not reset to the default");
        assert_eq!(saved.tcp_keepalive_secs, 99, "not reset to the default");
    }

    /// `Some` holding an empty array, never `None`. Filling the log needs the
    /// accept loop, so the non-empty path lives in the `blocked` unit tests and
    /// this pins the dispatch arms.
    fn an_empty_blocked_log_queries_as_an_empty_array() {
        let blocked = host_query(HostQueryType::RecentBlocked, &[]).expect("recent-blocked query");
        let entries: Vec<crate::ffi::dtos::BlockedConnection> =
            rmp_serde::from_slice(&blocked).unwrap();
        assert!(entries.is_empty());
        assert_eq!(
            host_command(HostCommandType::ClearBlockedLog, &[]),
            MbrcResult::Ok
        );
    }

    /// The bound port, not the one just written: a settings write does not
    /// hot-reload. The address list is interface-dependent, so only the port is
    /// pinned here.
    fn listening_addresses_reports_the_port_the_core_is_bound_to() {
        let listening =
            host_query(HostQueryType::ListeningAddresses, &[]).expect("listening query");
        let info: crate::ffi::dtos::ListeningInfo = rmp_serde::from_slice(&listening).unwrap();
        assert_eq!(info.port, 4321);
    }

    #[test]
    fn merge_applies_only_the_keys_the_payload_carries() {
        let base = Config {
            port: 3000,
            tcp_keepalive_secs: 99,
            allowed_addresses: vec!["10.0.0.1".to_string()],
            ..Config::default()
        };
        let merged = merge_settings(
            base,
            &serde_json::json!({"port": 7000, "allowed_addresses": ["10.0.0.2"]}),
        )
        .unwrap();
        assert_eq!(merged.port, 7000);
        assert_eq!(merged.allowed_addresses, vec!["10.0.0.2"]); // lists replace
        assert_eq!(merged.tcp_keepalive_secs, 99); // absent key untouched
    }

    #[test]
    fn stopping_is_set_for_teardown_and_cleared_for_a_restart() {
        let core = Core::new(Arc::new(NullProviders), Config::default());
        assert!(!core.is_stopping());

        core.begin_stopping();
        assert!(core.is_stopping());
        core.begin_stopping(); // idempotent: the host may say so more than once
        assert!(core.is_stopping());

        // Saving a port restarts networking on the same core; a flag that
        // survived that would silently stop every later cover build.
        core.clear_stopping();
        assert!(!core.is_stopping());
    }

    #[test]
    fn merge_rejects_a_non_map_payload_and_bad_field_types() {
        assert!(merge_settings(Config::default(), &serde_json::json!([1, 2])).is_err());
        assert!(
            merge_settings(
                Config::default(),
                &serde_json::json!({"port": "not a number"})
            )
            .is_err()
        );
    }

    #[test]
    fn merge_drops_keys_the_core_does_not_know() {
        // `debug` is the stale pre-`log_level` key: unknown on both sides.
        let merged = merge_settings(
            Config::default(),
            &serde_json::json!({"debug": true, "port": 4000}),
        )
        .unwrap();
        assert_eq!(merged.port, 4000);
        let json = serde_json::to_string(&merged).unwrap();
        assert!(!json.contains("debug"));
    }
}
