//! A marker that tells the next start whether this session ended cleanly.
//!
//! Written when the core starts and removed when it shuts down, so finding it
//! at start means the last session died: MusicBee crashed or was killed, which
//! leaves nothing in the log by itself. It also holds what the core was last
//! busy with, so the warning can say where it was.

use std::path::{Path, PathBuf};

/// The marker's file name in the storage directory.
const FILE_NAME: &str = "session.marker";

/// The marker for this session. Inert without a storage directory.
#[derive(Debug)]
pub struct SessionMarker {
    path: Option<PathBuf>,
}

impl SessionMarker {
    /// Opens the marker under `storage`, warning if the last session left one.
    pub fn open(storage: &str) -> Self {
        if storage.is_empty() {
            return Self { path: None };
        }
        let path = Path::new(storage).join(FILE_NAME);
        if let Ok(last) = std::fs::read_to_string(&path) {
            tracing::warn!(
                last = last.trim(),
                "the previous session did not shut down cleanly: MusicBee crashed or was closed forcibly"
            );
        }
        let marker = Self { path: Some(path) };
        marker.note("starting");
        marker
    }

    /// Records what the core is busy with, replacing what was there.
    pub fn note(&self, activity: &str) {
        if let Some(path) = &self.path
            && let Err(e) = std::fs::write(path, activity)
        {
            tracing::debug!(error = %e, "session marker: write failed");
        }
    }

    /// Marks a clean shutdown by removing the marker.
    pub fn close(&self) {
        if let Some(path) = &self.path {
            let _ = std::fs::remove_file(path);
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn dir(name: &str) -> PathBuf {
        let dir = std::env::temp_dir().join(format!("mbrc-session-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn a_clean_shutdown_leaves_no_marker() {
        let dir = dir("clean");
        let marker = SessionMarker::open(&dir.to_string_lossy());
        assert!(dir.join(FILE_NAME).exists());
        marker.close();
        assert!(!dir.join(FILE_NAME).exists());
    }

    #[test]
    fn a_session_that_died_leaves_what_it_was_doing() {
        let dir = dir("died");
        let marker = SessionMarker::open(&dir.to_string_lossy());
        marker.note("building the cover cache: album 4200 of 15266");
        drop(marker);
        let left = std::fs::read_to_string(dir.join(FILE_NAME)).unwrap();
        assert_eq!(left, "building the cover cache: album 4200 of 15266");

        let next = SessionMarker::open(&dir.to_string_lossy());
        assert_eq!(
            std::fs::read_to_string(dir.join(FILE_NAME)).unwrap(),
            "starting"
        );
        next.close();
    }

    #[test]
    fn without_a_storage_directory_it_does_nothing() {
        let marker = SessionMarker::open("");
        marker.note("anything");
        marker.close();
    }
}
