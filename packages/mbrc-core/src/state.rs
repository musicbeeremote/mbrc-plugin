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
use crate::server::{self, notifications, NetHandle, RebuildScope};
use crate::store::Db;

/// The initialized core: the provider boundary, config, the broadcast registry,
/// and the now-playing cache. Shared (via `Arc`) with the server thread and
/// notification handling.
pub struct Core {
    pub providers: Arc<dyn Providers>,
    pub config: Config,
    pub broadcaster: Broadcaster,
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
}

impl Core {
    pub fn new(providers: Arc<dyn Providers>, config: Config) -> Self {
        let now_playing = NowPlayingCache::new(providers.clone());
        // One shared redb store for both durable caches. Disabled (no-op) when
        // there's no storage path (unit/integration tests). Import a shipped
        // state.json once before the cover store reads from redb.
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
            now_playing,
            cover_store,
            metadata_cache,
            reconciling: AtomicBool::new(false),
            registry,
            blocked: BlockedLog::default(),
            conn_counter: AtomicU64::new(0),
            scanner_nudge: Arc::new(Notify::new()),
        }
    }

    /// A fresh per-connection id (used as the broadcast-registry key).
    pub fn next_conn_id(&self) -> u64 {
        self.conn_counter.fetch_add(1, Ordering::Relaxed)
    }

    /// Acquire the single-flight reconcile right: `true` if the caller may run
    /// `reconcile_library` (it must then call [`end_reconcile`](Self::end_reconcile)),
    /// `false` if one is already in progress.
    pub fn try_begin_reconcile(&self) -> bool {
        !self.reconciling.swap(true, Ordering::AcqRel)
    }

    /// Release the reconcile right acquired by [`try_begin_reconcile`](Self::try_begin_reconcile).
    pub fn end_reconcile(&self) {
        self.reconciling.store(false, Ordering::Release);
    }

    /// Whether a library reconcile / cache build is currently running. Surfaced
    /// to the settings panel's cache-status line.
    pub fn is_reconciling(&self) -> bool {
        self.reconciling.load(Ordering::Acquire)
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

/// Store the initialized core. `AlreadyInitialized` if called again without an
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

/// Serialize the initialized core's current settings as MessagePack (named maps,
/// the on-disk settable fields; `storage_path` is skipped). `None` if not
/// initialized. The settings panel reads this to populate its controls - Rust
/// owns the read. MessagePack (not JSON) so the host needs no JSON dependency;
/// the on-disk `core_settings.json` stays human-readable JSON separately.
pub fn read_settings_bytes() -> Option<Vec<u8>> {
    let guard = lock();
    let config = &guard.as_ref()?.core.config;
    // Named maps so the C# contractless resolver reads by property name.
    rmp_serde::to_vec_named(config).ok()
}

/// Validate and persist new settings (MessagePack from the host) to
/// `core_settings.json` in the core's storage dir - Rust owns the write. The
/// file stays JSON on disk; only the transport is MessagePack. The running core
/// is NOT hot-reloaded here; the host re-inits (when the change needs it) to
/// apply. Returns an error string on parse/validation/write failure.
pub fn write_settings_bytes(bytes: &[u8]) -> Result<(), String> {
    let storage = {
        let guard = lock();
        guard
            .as_ref()
            .ok_or("core not initialized")?
            .core
            .config
            .storage_path
            .clone()
    };
    let mut config: Config =
        rmp_serde::from_slice(bytes).map_err(|e| format!("invalid settings msgpack: {e}"))?;
    config.validate()?;
    config.storage_path = storage.clone();
    let pretty = serde_json::to_string_pretty(&config).map_err(|e| e.to_string())?;
    let path = std::path::Path::new(&storage).join("core_settings.json");
    std::fs::write(&path, pretty).map_err(|e| format!("write settings: {e}"))
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

/// Dispatch a host -> core query (request/response). Returns the MessagePack
/// result, or `None` when the core is not initialized or the handler has no
/// answer. The generic entry point for the C# host's app-level reads; add a
/// [`HostQueryType`] variant + arm here rather than a new FFI export.
pub fn host_query(kind: HostQueryType, _params: &[u8]) -> Option<Vec<u8>> {
    match kind {
        HostQueryType::CacheStatus => cache_status_bytes(),
        HostQueryType::RecentBlocked => recent_blocked_bytes(),
    }
}

/// Dispatch a host -> core command (fire-and-forget). The generic entry point
/// for the C# host's app-level actions; add a [`HostCommandType`] variant + arm.
pub fn host_command(kind: HostCommandType, _params: &[u8]) -> MbrcResult {
    match kind {
        HostCommandType::RebuildMetadata => rebuild(RebuildScope::Metadata),
        HostCommandType::RebuildCovers => rebuild(RebuildScope::Covers),
        HostCommandType::ClearBlockedLog => clear_blocked(),
    }
}

/// Serialize the current cache status as MessagePack for the settings panel.
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

/// Serialize the recent blocked-connection entries (newest first) as MessagePack
/// for the settings panel. `None` if the core is not initialized; an empty log
/// serializes to an empty array (not `None`).
fn recent_blocked_bytes() -> Option<Vec<u8>> {
    let core = {
        let guard = lock();
        guard.as_ref()?.core.clone()
    };
    rmp_serde::to_vec_named(&core.blocked.recent()).ok()
}

/// Clear the in-memory blocked-connection log (the panel's "Clear" button).
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

/// Kick a background rebuild of the requested cache (the settings panel's per-
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

/// Build and fan out the broadcast frames for a MusicBee notification.
pub fn handle_notification(ntype: NotificationType) -> MbrcResult {
    // Clone the Arc and drop the lock before querying/broadcasting.
    let core = {
        let guard = lock();
        match guard.as_ref() {
            Some(runtime) => runtime.core.clone(),
            None => return MbrcResult::NotInitialized,
        }
    };

    // Library-changing notifications maintain the metadata cache. Handled here
    // (not in the pure `on_notification`) because the switch needs the owned
    // `Arc<Core>` to spawn the reconcile on a plain thread - the C# notification
    // thread has no Tokio runtime, and the reconcile does blocking FFI.
    match ntype {
        NotificationType::LibrarySwitched => {
            // Gate reads off + clear immediately so nothing stale is served in
            // the gap; the reconcile re-fingerprints, re-prewarms, re-validates.
            core.metadata_cache.invalidate();
            let reconcile_core = core.clone();
            std::thread::spawn(move || server::reconcile_library(&reconcile_core));
            return MbrcResult::Ok;
        }
        NotificationType::FileAddedToLibrary => {
            // A file changed the library: nudge the background Scanner to run a
            // delta sooner (it rebuilds the ordinal index and drops changed
            // tracks' cached tags). Debounced there, so a big import that fires
            // this per-file collapses to a scan or two - NOT a full cache clear
            // per file (which would wipe the whole ordinal index each time).
            core.scanner_nudge.notify_one();
        }
        _ => {}
    }

    let frames = notifications::on_notification(&core, ntype);
    core.broadcaster.broadcast(&frames);
    MbrcResult::Ok
}

/// Stop networking (if running) and drop the core, allowing a later re-init.
pub fn shutdown() -> MbrcResult {
    let mut guard = lock();
    match guard.take() {
        Some(runtime) => {
            if let Some(net) = runtime.net {
                net.stop();
            }
            MbrcResult::Ok
        }
        None => MbrcResult::NotInitialized,
    }
}

/// Start the TCP server + discovery responder.
pub fn start_networking() -> MbrcResult {
    let mut guard = lock();
    let Some(runtime) = guard.as_mut() else {
        return MbrcResult::NotInitialized;
    };
    if runtime.net.is_some() {
        return MbrcResult::AlreadyRunning;
    }
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

/// Stop the TCP server + discovery responder (leaves the core initialized).
pub fn stop_networking() -> MbrcResult {
    let mut guard = lock();
    let Some(runtime) = guard.as_mut() else {
        return MbrcResult::NotInitialized;
    };
    match runtime.net.take() {
        Some(net) => {
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
    fn settings_round_trip_through_state() {
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

        // Read reflects the in-memory config as MessagePack; round-trips back to
        // a Config with the same port and no storage_path.
        let bytes = read_settings_bytes().expect("read settings");
        let echoed: Config = rmp_serde::from_slice(&bytes).unwrap();
        assert_eq!(echoed.port, 4321);
        assert_eq!(echoed.storage_path, ""); // skipped, not exposed

        // A valid write (MessagePack in) persists core_settings.json as JSON.
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

        // An invalid write (port 0) is refused; the file is unchanged.
        let bad = rmp_serde::to_vec_named(&Config {
            port: 0,
            ..Config::default()
        })
        .unwrap();
        assert!(write_settings_bytes(&bad).is_err());

        let _ = shutdown();
    }
}
