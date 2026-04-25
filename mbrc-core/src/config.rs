//! Runtime configuration for the Rust core.
//!
//! C# WinForms owns the canonical settings UI and persists them to
//! `settings.xml`. On save, C# also writes a JSON projection of the
//! Rust-relevant subset to `core_settings.json` in the same storage
//! directory. The Rust core reads that file once at server start.
//!
//! This is the only direction the data flows — Rust never writes
//! settings. If the file is missing or unparseable we fall back to
//! safe defaults and log; the user's WinForms settings still win
//! whenever they hit Save next.

use std::fs;
use std::path::Path;

use serde::{Deserialize, Serialize};
use tracing::{info, warn};

pub const FILENAME: &str = "core_settings.json";

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CoreSettings {
    /// TCP port the hybrid server listens on.
    pub port: u16,
}

impl Default for CoreSettings {
    fn default() -> Self {
        Self { port: 3000 }
    }
}

impl CoreSettings {
    /// Read `core_settings.json` from the given storage directory,
    /// falling back to `Default` if the file is missing or invalid.
    pub fn load_from_storage(storage_path: &str) -> Self {
        let path = Path::new(storage_path).join(FILENAME);
        match fs::read_to_string(&path) {
            Ok(s) => match serde_json::from_str::<Self>(&s) {
                Ok(cfg) => {
                    info!(?cfg, path = %path.display(), "loaded core settings");
                    cfg
                }
                Err(e) => {
                    warn!(
                        path = %path.display(),
                        "failed to parse core_settings.json ({}); using defaults",
                        e
                    );
                    Self::default()
                }
            },
            Err(_) => {
                info!(path = %path.display(), "core_settings.json not found; using defaults");
                Self::default()
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::io::Write;

    #[test]
    fn defaults_are_safe() {
        let d = CoreSettings::default();
        assert_eq!(d.port, 3000);
    }

    #[test]
    fn missing_file_yields_default() {
        let dir = std::env::temp_dir().join(format!("mbrc-cfg-test-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&dir).unwrap();
        let cfg = CoreSettings::load_from_storage(dir.to_str().unwrap());
        assert_eq!(cfg.port, 3000);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn valid_json_round_trips() {
        let dir = std::env::temp_dir().join(format!("mbrc-cfg-test-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(FILENAME);
        let mut f = std::fs::File::create(&path).unwrap();
        f.write_all(br#"{"port": 4242}"#).unwrap();
        drop(f);

        let cfg = CoreSettings::load_from_storage(dir.to_str().unwrap());
        assert_eq!(cfg.port, 4242);
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn corrupt_file_yields_default() {
        let dir = std::env::temp_dir().join(format!("mbrc-cfg-test-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.join(FILENAME);
        std::fs::write(&path, b"not json at all").unwrap();

        let cfg = CoreSettings::load_from_storage(dir.to_str().unwrap());
        assert_eq!(cfg.port, 3000);
        let _ = std::fs::remove_dir_all(&dir);
    }
}
