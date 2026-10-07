//! One now-playing list broadcast per burst of MusicBee's list notifications.
//!
//! MusicBee reports every step of a queue edit: "play now" is a clear, a queue
//! and a play, three notifications inside a few milliseconds, and a client
//! re-reads the whole list on each broadcast. The list `version` still moves on
//! every notification, so a write against a stale list is refused as before;
//! only the broadcast waits for the burst to settle.

use std::sync::Arc;
use std::sync::atomic::{AtomicBool, Ordering};
use std::time::Duration;

use tokio::sync::Notify;

use super::notifications;
use crate::ffi::types::NotificationType;
use crate::state::Core;

/// How long after the first notification of a burst the change is broadcast.
pub const SETTLE: Duration = Duration::from_millis(150);

/// A list change waiting to be broadcast.
#[derive(Default)]
pub struct PendingListChange {
    pending: AtomicBool,
    nudge: Notify,
}

impl PendingListChange {
    /// Records a change; the broadcast follows once the burst settles.
    pub fn mark(&self) {
        self.pending.store(true, Ordering::Release);
        self.nudge.notify_one();
    }

    fn take(&self) -> bool {
        self.pending.swap(false, Ordering::AcqRel)
    }
}

/// Broadcasts list changes until `shutdown` fires.
pub async fn run(core: Arc<Core>, shutdown: Arc<Notify>) {
    loop {
        tokio::select! {
            _ = shutdown.notified() => return,
            _ = core.list_change.nudge.notified() => {
                tokio::time::sleep(SETTLE).await;
                if core.list_change.take() {
                    broadcast(&core);
                }
            }
        }
    }
}

fn broadcast(core: &Core) {
    let (v4, v6) = notifications::on_notification(core, NotificationType::NowPlayingListChanged);
    core.broadcaster.broadcast(&v4);
    core.v6_broadcaster.broadcast(&v6);
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::Config;
    use crate::providers::NullProviders;
    use crate::state::dispatch_notification;

    fn drain(rx: &mut tokio::sync::mpsc::UnboundedReceiver<String>) -> Vec<String> {
        std::iter::from_fn(|| rx.try_recv().ok()).collect()
    }

    /// "Play now" is three list notifications; the client should re-read once.
    #[tokio::test]
    async fn a_burst_of_list_notifications_is_broadcast_once() {
        let core = Arc::new(Core::new(Arc::new(NullProviders), Config::for_test(0)));
        let (v4_tx, mut v4) = tokio::sync::mpsc::unbounded_channel();
        let (v6_tx, mut v6) = tokio::sync::mpsc::unbounded_channel();
        core.broadcaster.register(1, v4_tx);
        core.v6_broadcaster.register(1, v6_tx);
        let shutdown = Arc::new(Notify::new());
        let task = tokio::spawn(run(core.clone(), shutdown.clone()));
        let version = core.now_playing.list_version();

        for _ in 0..3 {
            dispatch_notification(&core, NotificationType::NowPlayingListChanged, None);
        }
        assert_eq!(core.now_playing.list_version(), version + 3);
        assert!(drain(&mut v4).is_empty(), "sent before the burst settled");

        tokio::time::sleep(SETTLE * 3).await;
        assert_eq!(drain(&mut v4).len(), 1);
        assert_eq!(drain(&mut v6).len(), 1);

        dispatch_notification(&core, NotificationType::NowPlayingListChanged, None);
        tokio::time::sleep(SETTLE * 3).await;
        assert_eq!(drain(&mut v4).len(), 1, "a later change is sent too");
        task.abort();
    }
}
