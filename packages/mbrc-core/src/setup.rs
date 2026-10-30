//! First-run setup: when the host opens its setup wizard, and what the wizard
//! asks the core.
//!
//! The wizard opens while the stored `setup_version` is below
//! [`CURRENT_SETUP_VERSION`]. Every way into 2.0 starts at 0: a fresh install has
//! no settings file, a 1.x upgrade gets a migrated one without the field, and an
//! earlier Rust-core build wrote a file without it. Finishing or skipping stores
//! the current version, so a release that adds a step bumps the constant and only
//! that step's audience sees the wizard again.

use std::path::Path;
use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};

use crate::config::Config;
use crate::ffi::dtos::{SetupChoices, SetupStatus};
use crate::ffi::types::MbrcResult;

/// The wizard version this build ships.
pub const CURRENT_SETUP_VERSION: u32 = 1;

static EXISTING_INSTALL: AtomicBool = AtomicBool::new(false);

/// The version recorded since start-up; the config the core runs on was read before it.
static COMPLETED_VERSION: AtomicU32 = AtomicU32::new(0);

/// Record whether settings existed before this start, for the "carried over" line.
pub fn note_existing_install(storage_path: &str) {
    EXISTING_INSTALL.store(has_saved_settings(storage_path), Ordering::Relaxed);
}

/// Settings from an earlier install: the core's own file, or the 1.x `settings.xml`.
fn has_saved_settings(storage_path: &str) -> bool {
    let dir = Path::new(storage_path);
    dir.join("core_settings.json").exists() || dir.join("settings.xml").exists()
}

/// Whether the host should open the wizard for `config`.
pub fn is_needed(config: &Config) -> bool {
    config
        .setup_version
        .max(COMPLETED_VERSION.load(Ordering::Relaxed))
        < CURRENT_SETUP_VERSION
}

/// The status for the running core, as MessagePack; `None` before init.
pub fn status_bytes() -> Option<Vec<u8>> {
    let core = crate::state::core_handle()?;
    let status = SetupStatus {
        needed: is_needed(&core.config),
        upgrade: EXISTING_INSTALL.load(Ordering::Relaxed),
        connected_devices: i32::try_from(core.registry.remote_connections()).unwrap_or(i32::MAX),
    };
    rmp_serde::to_vec_named(&status).ok()
}

/// Store the current wizard version, with the user's choices when they finished.
///
/// `params` is an optional MessagePack [`SetupChoices`]: empty for Skip. Both go
/// in one settings write, so a rejected write leaves setup not done.
pub fn complete(params: &[u8]) -> MbrcResult {
    let patch = match completion_patch(params) {
        Ok(patch) => patch,
        Err(error) => {
            tracing::warn!(%error, "ignoring malformed setup choices");
            return MbrcResult::InvalidArgument;
        }
    };
    match crate::state::write_settings_patch(&patch) {
        Ok(()) => {
            COMPLETED_VERSION.store(CURRENT_SETUP_VERSION, Ordering::Relaxed);
            MbrcResult::Ok
        }
        Err(error) => {
            tracing::warn!(%error, "could not record that setup was done");
            MbrcResult::RuntimeError
        }
    }
}

/// The settings patch for a completion: the version, plus the choices when given.
fn completion_patch(params: &[u8]) -> Result<serde_json::Value, String> {
    let mut patch = serde_json::json!({ "setup_version": CURRENT_SETUP_VERSION });
    if params.is_empty() {
        return Ok(patch);
    }
    let choices: SetupChoices = rmp_serde::from_slice(params).map_err(|e| e.to_string())?;
    let object = patch.as_object_mut().expect("a JSON object literal");
    object.insert("web_enabled".into(), choices.web_enabled.into());
    object.insert("web_auth_required".into(), choices.web_auth_required.into());
    object.insert(
        "update_check_enabled".into(),
        choices.update_check_enabled.into(),
    );
    object.insert("update_channel".into(), choices.update_channel.into());
    Ok(patch)
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::config::migrate_legacy_settings;

    fn scratch(name: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("mbrc-setup-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    fn loaded_after_migration(dir: &Path) -> Config {
        let path = dir.to_str().unwrap();
        migrate_legacy_settings(path);
        Config::load(path)
    }

    #[test]
    fn a_fresh_install_needs_setup_and_is_not_an_upgrade() {
        let dir = scratch("fresh");

        assert!(!has_saved_settings(dir.to_str().unwrap()));
        assert!(is_needed(&loaded_after_migration(&dir)));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_migrated_1x_install_needs_setup_and_is_an_upgrade() {
        let dir = scratch("legacy");
        std::fs::write(
            dir.join("settings.xml"),
            "<mbremote><port>3456</port><selection>All</selection></mbremote>",
        )
        .unwrap();

        assert!(has_saved_settings(dir.to_str().unwrap()));
        let config = loaded_after_migration(&dir);
        assert_eq!(config.port, 3456);
        assert!(is_needed(&config));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn an_earlier_core_settings_file_without_the_field_needs_setup() {
        let dir = scratch("earlier-core");
        std::fs::write(dir.join("core_settings.json"), r#"{"port":3001}"#).unwrap();

        assert!(has_saved_settings(dir.to_str().unwrap()));
        assert!(is_needed(&loaded_after_migration(&dir)));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_file_at_the_current_version_does_not_need_setup() {
        let dir = scratch("current");
        std::fs::write(
            dir.join("core_settings.json"),
            format!(r#"{{"setup_version":{CURRENT_SETUP_VERSION}}}"#),
        )
        .unwrap();

        assert!(!is_needed(&loaded_after_migration(&dir)));
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn skip_records_only_the_version() {
        let patch = completion_patch(&[]).unwrap();

        assert_eq!(
            patch,
            serde_json::json!({ "setup_version": CURRENT_SETUP_VERSION })
        );
    }

    #[test]
    fn finish_records_the_choices_with_the_version() {
        let choices = SetupChoices {
            web_enabled: false,
            web_auth_required: true,
            update_check_enabled: true,
            update_channel: "testing".into(),
        };

        let patch = completion_patch(&rmp_serde::to_vec_named(&choices).unwrap()).unwrap();

        assert_eq!(patch["setup_version"], CURRENT_SETUP_VERSION);
        assert_eq!(patch["web_enabled"], false);
        assert_eq!(patch["web_auth_required"], true);
        assert_eq!(patch["update_check_enabled"], true);
        assert_eq!(patch["update_channel"], "testing");
    }

    #[test]
    fn malformed_choices_are_refused() {
        assert!(completion_patch(&[0xc1]).is_err());
    }

    #[test]
    fn a_later_wizard_version_asks_an_install_that_finished_an_earlier_one() {
        let finished_earlier = Config {
            setup_version: CURRENT_SETUP_VERSION - 1,
            ..Config::default()
        };

        assert!(is_needed(&finished_earlier));
    }
}
