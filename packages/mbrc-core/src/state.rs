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

    /// Acquire the single-flight reconcile right, or `None` if one is already in
    /// progress.
    ///
    /// A guard rather than a pair of calls, because the work it spans is long
    /// (a library scan, blocking calls into MusicBee, and a thread pool doing
    /// the cover build) and the cost of not releasing it is silent and
    /// permanent: no further rebuild would run for the rest of the session, a
    /// library switch would be ignored, and the settings panel would sit on
    /// "Rebuilding cache..." forever. Dropping is the one thing that happens on
    /// every path out, including a panic.
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
///
/// The write is a **merge**, not a replace: the host's payload is applied key by
/// key over the config currently on disk. The panel only knows a subset of
/// [`Config`]'s fields, and deserializing its payload straight into a fresh
/// `Config` would reset every field it omits back to its default - so saving the
/// panel would silently wipe any Rust-only setting.
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

/// Hand the staged update to the elevated helper.
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
    // Falling back to the core's own version keeps a host that cannot answer
    // from turning into an unconditional refusal; the two are stamped from the
    // same `Directory.Build.props`.
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

/// Apply the host's settings payload over `base`, field by field.
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

/// Dispatch a host -> core query (request/response). Returns the MessagePack
/// result, or `None` when the core is not initialized or the handler has no
/// answer. The generic entry point for the C# host's app-level reads; add a
/// [`HostQueryType`] variant + arm here rather than a new FFI export.
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

/// Dispatch a host -> core command (fire-and-forget). The generic entry point
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

/// Decode a capture command's params. An empty payload is a valid request with
/// nothing in it (`CancelCapture` sends one), so it is not an error.
fn capture_request(params: &[u8]) -> Result<crate::ffi::dtos::CaptureRequest, String> {
    if params.is_empty() {
        return Ok(crate::ffi::dtos::CaptureRequest::default());
    }
    rmp_serde::from_slice(params).map_err(|e| e.to_string())
}

/// Run `action` against the initialized core, or report `NotInitialized`. The
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

/// Serialize the addresses a client can reach the server on (candidate interface
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

/// Serialize where the update flow stands as MessagePack for the settings panel.
/// `None` if the core is not initialized; every other state is a real answer,
/// including "nothing has been checked yet".
fn update_status_bytes() -> Option<Vec<u8>> {
    let core = {
        let guard = lock();
        guard.as_ref()?.core.clone()
    };
    crate::updates::service::status_bytes(&core)
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

        // A panel-shaped payload (the 8 fields the C# `CoreSettings` DTO carries)
        // must not disturb the fields it doesn't know about. Seed the file with
        // non-default values for two of those, then save from the "panel".
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
        assert_eq!(saved.port, 6001); // the panel's edit landed
        assert_eq!(saved.log_level, crate::config::LogLevel::Debug);
        assert_eq!(saved.bind_address, "127.0.0.1"); // not clobbered back to 0.0.0.0
        assert_eq!(saved.tcp_keepalive_secs, 99); // nor to its default

        // Blocked-connection host dispatch (folded in here to avoid a second
        // concurrent STATE test): an empty log queries to an empty array - Some,
        // not None - and the clear command succeeds. Populating the log needs the
        // accept loop, so the non-empty path is covered by the `blocked` unit
        // tests; this pins the dispatch arms + the empty-not-None contract.
        let blocked = host_query(HostQueryType::RecentBlocked, &[]).expect("recent-blocked query");
        let entries: Vec<crate::ffi::dtos::BlockedConnection> =
            rmp_serde::from_slice(&blocked).unwrap();
        assert!(entries.is_empty());
        assert_eq!(
            host_command(HostCommandType::ClearBlockedLog, &[]),
            MbrcResult::Ok
        );

        // Listening-addresses host dispatch: reports the in-memory bound port
        // (4321, not the 5555 just written - a write doesn't hot-reload). The
        // address list is interface-dependent (empty on a loopback-only runner),
        // so only the port is pinned here.
        let listening =
            host_query(HostQueryType::ListeningAddresses, &[]).expect("listening query");
        let info: crate::ffi::dtos::ListeningInfo = rmp_serde::from_slice(&listening).unwrap();
        assert_eq!(info.port, 4321);

        let _ = shutdown();
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
    fn merge_rejects_a_non_map_payload_and_bad_field_types() {
        assert!(merge_settings(Config::default(), &serde_json::json!([1, 2])).is_err());
        assert!(merge_settings(
            Config::default(),
            &serde_json::json!({"port": "not a number"})
        )
        .is_err());
    }

    #[test]
    fn merge_drops_keys_the_core_does_not_know() {
        // A stale key on disk (the pre-`log_level` `debug` bool) is not carried
        // through the merge, and an unknown key in the payload is ignored rather
        // than written back.
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
