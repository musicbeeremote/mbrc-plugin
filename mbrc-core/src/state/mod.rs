use std::sync::{Arc, OnceLock};

use tokio::runtime::Runtime;
use tokio::sync::{broadcast, oneshot};
use tracing::{error, info};

use crate::ffi::callbacks::SafeCallbacks;
use crate::ffi::types::MbrcResult;
use crate::protocol::messages::BroadcastEvent;

/// Global singleton for the Rust core. Enforces single initialization via `OnceLock`.
static CORE: OnceLock<MbrcCore> = OnceLock::new();

/// Top-level container owning the tokio runtime and shared application state.
pub struct MbrcCore {
    runtime: Runtime,
    state: Arc<AppState>,
}

/// Shared state accessible from async tasks. Wrapped in `Arc` for cheap cloning.
#[allow(dead_code)]
pub struct AppState {
    callbacks: SafeCallbacks,
    storage_path: String,
    /// Sender to trigger graceful HTTP server shutdown.
    /// Wrapped in parking_lot::Mutex because oneshot::Sender is not Clone and we need
    /// interior mutability to take() it once.
    shutdown_tx: parking_lot::Mutex<Option<oneshot::Sender<()>>>,
    /// Whether the networking (HTTP server) is currently running.
    networking_running: parking_lot::Mutex<bool>,
    /// Broadcast channel for pushing events to all connected legacy TCP clients.
    event_tx: broadcast::Sender<BroadcastEvent>,
}

#[allow(dead_code)]
impl AppState {
    pub fn callbacks(&self) -> &SafeCallbacks {
        &self.callbacks
    }

    pub fn storage_path(&self) -> &str {
        &self.storage_path
    }

    pub fn take_shutdown_tx(&self) -> Option<oneshot::Sender<()>> {
        self.shutdown_tx.lock().take()
    }

    pub fn set_shutdown_tx(&self, tx: oneshot::Sender<()>) {
        *self.shutdown_tx.lock() = Some(tx);
    }

    pub fn is_networking_running(&self) -> bool {
        *self.networking_running.lock()
    }

    pub fn set_networking_running(&self, running: bool) {
        *self.networking_running.lock() = running;
    }

    pub fn event_tx(&self) -> &broadcast::Sender<BroadcastEvent> {
        &self.event_tx
    }

    /// Build a fresh `Arc<AppState>` without touching the `MbrcCore` singleton.
    /// Used by the golden-trace replay harness so each fixture run starts clean.
    pub fn for_replay(callbacks: SafeCallbacks, storage_path: String) -> Arc<Self> {
        let (event_tx, _) = broadcast::channel::<BroadcastEvent>(256);
        Arc::new(AppState {
            callbacks,
            storage_path,
            shutdown_tx: parking_lot::Mutex::new(None),
            networking_running: parking_lot::Mutex::new(false),
            event_tx,
        })
    }
}

#[allow(dead_code)]
impl MbrcCore {
    /// Initialize the global core. Returns `Err` if already initialized.
    pub fn initialize(callbacks: SafeCallbacks, storage_path: String) -> Result<(), MbrcResult> {
        let runtime = Runtime::new().map_err(|e| {
            error!("Failed to create tokio runtime: {}", e);
            MbrcResult::RuntimeError
        })?;

        let (event_tx, _) = broadcast::channel::<BroadcastEvent>(256);

        let state = Arc::new(AppState {
            callbacks,
            storage_path,
            shutdown_tx: parking_lot::Mutex::new(None),
            networking_running: parking_lot::Mutex::new(false),
            event_tx,
        });

        let core = MbrcCore { runtime, state };

        CORE.set(core).map_err(|_| {
            error!("MbrcCore already initialized");
            MbrcResult::AlreadyInitialized
        })?;

        info!("MbrcCore initialized");
        Ok(())
    }

    /// Get a reference to the global core, or `Err` if not initialized.
    pub fn get() -> Result<&'static MbrcCore, MbrcResult> {
        CORE.get().ok_or(MbrcResult::NotInitialized)
    }

    pub fn runtime(&self) -> &Runtime {
        &self.runtime
    }

    pub fn state(&self) -> &Arc<AppState> {
        &self.state
    }

    /// Start the HTTP server on the given port.
    pub fn start_networking(&self, port: u16) -> Result<(), MbrcResult> {
        if self.state.is_networking_running() {
            return Err(MbrcResult::AlreadyRunning);
        }

        let (tx, rx) = oneshot::channel::<()>();
        self.state.set_shutdown_tx(tx);

        let state = Arc::clone(&self.state);
        self.runtime.spawn(async move {
            if let Err(e) = crate::server::run(state.clone(), port, rx).await {
                error!("HTTP server error: {}", e);
            }
            state.set_networking_running(false);
            info!("HTTP server stopped");
        });

        self.state.set_networking_running(true);
        info!(port, "HTTP server starting");
        Ok(())
    }

    /// Stop the HTTP server gracefully.
    pub fn stop_networking(&self) -> Result<(), MbrcResult> {
        if !self.state.is_networking_running() {
            return Err(MbrcResult::NotRunning);
        }

        if let Some(tx) = self.state.take_shutdown_tx() {
            let _ = tx.send(());
            info!("Shutdown signal sent to HTTP server");
        }

        Ok(())
    }

    /// Shut down the entire core: stop networking, then drop the runtime.
    /// This consumes the core. Since it's behind OnceLock we can't truly drop it,
    /// but we can stop all background work.
    pub fn shutdown(&self) {
        info!("MbrcCore shutting down");
        let _ = self.stop_networking();
        // The runtime will be dropped when the process exits.
        // We don't call runtime.shutdown_background() here because OnceLock
        // doesn't let us take ownership. The process is exiting anyway.
    }
}
