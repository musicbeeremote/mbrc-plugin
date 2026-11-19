//! Threads the core starts for work that outlives the call that asked for it.
//!
//! Each one is tracked so `shutdown` can wait for it. A thread still inside the
//! core when MusicBee drops the callbacks it calls, or unloads `mbrc_core.dll`
//! on uninstall, takes MusicBee down with it; and one still holding `mbrc.redb`
//! makes the next core's open wait. Long jobs check `Core::is_stopping` and end
//! early, so the wait is short in practice and bounded either way.

use std::sync::Mutex;
use std::thread::JoinHandle;
use std::time::{Duration, Instant};

/// How often a bounded join looks again at a worker that has not finished.
const JOIN_POLL: Duration = Duration::from_millis(10);

struct Worker {
    name: &'static str,
    handle: JoinHandle<()>,
}

/// The core's background threads, joined at shutdown.
#[derive(Default)]
pub struct Workers {
    running: Mutex<Vec<Worker>>,
}

impl Workers {
    /// Starts `work` on a thread named `mbrc-<name>`; false when the thread
    /// could not be started.
    pub fn spawn(&self, name: &'static str, work: impl FnOnce() + Send + 'static) -> bool {
        let mut running = self.lock();
        running.retain(|w| !w.handle.is_finished());
        match std::thread::Builder::new()
            .name(format!("mbrc-{name}"))
            .spawn(work)
        {
            Ok(handle) => {
                running.push(Worker { name, handle });
                true
            }
            Err(e) => {
                tracing::warn!(worker = name, error = %e, "background thread not started");
                false
            }
        }
    }

    /// Waits up to `within` for every worker started so far, returning the
    /// names of those still running when it gave up.
    pub fn join(&self, within: Duration) -> Vec<&'static str> {
        let workers = std::mem::take(&mut *self.lock());
        let deadline = Instant::now() + within;
        let mut still_running = Vec::new();
        for worker in workers {
            while !worker.handle.is_finished() && Instant::now() < deadline {
                std::thread::sleep(JOIN_POLL);
            }
            if worker.handle.is_finished() {
                let _ = worker.handle.join();
            } else {
                still_running.push(worker.name);
            }
        }
        still_running
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, Vec<Worker>> {
        self.running.lock().unwrap_or_else(|e| e.into_inner())
    }
}

#[cfg(test)]
mod tests {
    use std::sync::Arc;
    use std::sync::atomic::{AtomicBool, Ordering};

    use super::*;

    #[test]
    fn a_join_waits_for_a_worker_to_finish() {
        let workers = Workers::default();
        let done = Arc::new(AtomicBool::new(false));
        let flag = done.clone();
        assert!(workers.spawn("test", move || {
            std::thread::sleep(Duration::from_millis(50));
            flag.store(true, Ordering::SeqCst);
        }));
        assert!(workers.join(Duration::from_secs(5)).is_empty());
        assert!(done.load(Ordering::SeqCst));
    }

    #[test]
    fn a_join_gives_up_at_its_deadline_and_names_the_stragglers() {
        let workers = Workers::default();
        let release = Arc::new(AtomicBool::new(false));
        let wait = release.clone();
        workers.spawn("slow", move || {
            while !wait.load(Ordering::SeqCst) {
                std::thread::sleep(Duration::from_millis(5));
            }
        });
        let started = Instant::now();
        assert_eq!(workers.join(Duration::from_millis(100)), ["slow"]);
        assert!(started.elapsed() < Duration::from_secs(2));
        release.store(true, Ordering::SeqCst);
    }

    #[test]
    fn finished_workers_are_not_kept() {
        let workers = Workers::default();
        workers.spawn("quick", || {});
        std::thread::sleep(Duration::from_millis(50));
        workers.spawn("next", || {});
        assert!(workers.lock().len() <= 2);
        assert!(workers.join(Duration::from_secs(5)).is_empty());
        assert!(workers.lock().is_empty());
    }
}
