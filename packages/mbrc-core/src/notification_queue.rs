//! MusicBee notifications, handled in arrival order on a thread of the core's own.
//!
//! MusicBee raises a notification on its own thread and waits for the plugin to
//! return. Handling a track change reads six host queries back through the
//! bridge, lyrics among them, so handling it on that thread made MusicBee wait on
//! its own API. The queue returns at once and a single worker keeps the order, so
//! a play-state change is never handled after the track change that followed it.

use std::sync::mpsc;
use std::time::Duration;

use crate::ffi::types::NotificationType;

/// How long a shutdown waits for queued notifications before leaving them.
const DRAIN_GRACE: Duration = Duration::from_secs(2);

type Notification = (NotificationType, Option<String>);

/// The handle MusicBee's notifications are pushed through.
pub struct NotificationQueue {
    sender: mpsc::Sender<Notification>,
    drained: mpsc::Receiver<()>,
}

impl NotificationQueue {
    /// Starts the worker that runs `handle` once per notification, in order.
    ///
    /// `handle` is dropped before the worker reports itself drained, so whatever
    /// it holds is released by the time [`NotificationQueue::stop`] returns.
    ///
    /// # Errors
    /// The worker thread could not be started.
    pub fn start(
        mut handle: impl FnMut(NotificationType, Option<String>) + Send + 'static,
    ) -> std::io::Result<Self> {
        let (sender, receiver) = mpsc::channel::<Notification>();
        let (drained_tx, drained) = mpsc::channel();
        std::thread::Builder::new()
            .name("mbrc-notifications".into())
            .spawn(move || {
                for (kind, path) in receiver {
                    let handled = std::panic::catch_unwind(std::panic::AssertUnwindSafe(|| {
                        handle(kind, path);
                    }));
                    if handled.is_err() {
                        tracing::warn!(?kind, "notification dropped after a panic");
                    }
                }
                drop(handle);
                let _ = drained_tx.send(());
            })?;
        Ok(Self { sender, drained })
    }

    /// Queues a notification for the worker and returns at once.
    pub fn push(&self, kind: NotificationType, path: Option<String>) {
        // Fails only once the worker is gone, when nothing is left to tell.
        let _ = self.sender.send((kind, path));
    }

    /// Takes no more notifications and waits up to `DRAIN_GRACE` for the queued ones.
    ///
    /// Bounded because a host call stuck behind a closing MusicBee must not hold
    /// up the close; the worker finishes on its own if it outlasts the grace.
    pub fn stop(self) {
        drop(self.sender);
        if let Err(mpsc::RecvTimeoutError::Timeout) = self.drained.recv_timeout(DRAIN_GRACE) {
            tracing::warn!("notifications still being handled at shutdown");
        }
    }
}

#[cfg(test)]
mod tests {
    use std::sync::{Arc, Barrier, Mutex};

    use super::*;

    type Seen = Arc<Mutex<Vec<(i32, Option<String>)>>>;

    /// A queue whose handler records each notification, or panics on `panic_on`.
    fn recording_queue(panic_on: Option<NotificationType>) -> (NotificationQueue, Seen) {
        let seen = Seen::default();
        let record = seen.clone();
        let queue = NotificationQueue::start(move |kind, path| {
            assert_ne!(Some(kind), panic_on, "expected panic");
            record.lock().unwrap().push((kind as i32, path));
        })
        .unwrap();
        (queue, seen)
    }

    #[test]
    fn notifications_are_handled_in_the_order_they_arrive() {
        let (queue, seen) = recording_queue(None);
        for raw in 0..=15 {
            queue.push(
                NotificationType::from_i32(raw).unwrap(),
                Some(raw.to_string()),
            );
        }
        queue.stop();
        let expected: Vec<_> = (0..=15).map(|raw| (raw, Some(raw.to_string()))).collect();
        assert_eq!(*seen.lock().unwrap(), expected);
    }

    #[test]
    fn push_returns_while_the_handler_is_still_busy() {
        let release = Arc::new(Barrier::new(2));
        let held = release.clone();
        let queue = NotificationQueue::start(move |_, _| {
            held.wait();
        })
        .unwrap();
        queue.push(NotificationType::TrackChanged, None);
        queue.push(NotificationType::PlayStateChanged, None);
        release.wait();
        release.wait();
        queue.stop();
    }

    #[test]
    fn a_panicking_handler_does_not_stop_later_notifications() {
        let (queue, seen) = recording_queue(Some(NotificationType::TrackChanged));
        queue.push(NotificationType::TrackChanged, None);
        queue.push(NotificationType::PlayStateChanged, None);
        queue.stop();
        let kinds: Vec<i32> = seen.lock().unwrap().iter().map(|(k, _)| *k).collect();
        assert_eq!(kinds, vec![NotificationType::PlayStateChanged as i32]);
    }

    #[test]
    fn stop_returns_after_the_handler_and_what_it_holds_are_dropped() {
        let held = Arc::new(());
        let inside = held.clone();
        let queue = NotificationQueue::start(move |_, _| {
            let _ = &inside;
        })
        .unwrap();
        queue.push(NotificationType::TrackChanged, None);
        queue.stop();
        assert_eq!(Arc::strong_count(&held), 1);
    }
}
