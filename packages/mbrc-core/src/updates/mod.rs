//! The core's side of the update pipeline: settings in, staged update out.
//!
//! The decisions all live in `mbrc-release` (`check`, `stage`, `UpdateState`).
//! What is here is the plumbing that crate deliberately does not know about: the
//! user's preferences, the storage directory, and the version the running plugin
//! reports over FFI.
//!
//! Nothing here reaches the network on its own. It takes an
//! [`HttpClient`](mbrc_release::HttpClient); `http_client` builds the
//! production one (WinHTTP) from the user's settings, and [`service`] is what
//! decides when to hand it one - the panel's buttons and the one check the core
//! runs shortly after networking starts.

pub mod elevate;
pub mod service;

use mbrc_release::{
    check::{self, CheckOptions, CheckOutcome},
    stage, AvailableUpdate, HttpClient, Result, StagedUpdate, UpdateState,
};
use time::OffsetDateTime;

use crate::config::Config;

/// The product version this core was built as, from `Directory.Build.props`.
///
/// The update check compares against the version the *plugin* reports over FFI;
/// this is what the core says about itself in the log.
pub const CORE_VERSION: &str = env!("MBRC_VERSION");

/// Builds the production HTTP client from the user's settings.
///
/// The `User-Agent` is not decoration: GitHub rejects requests without one, and
/// carrying the version means a rate-limit complaint can be traced to a build
/// rather than to "some plugin". `proxy_override` is passed straight through -
/// empty means WinHTTP auto-detects, which is what almost everyone gets.
///
/// # Errors
/// The WinHTTP session could not be opened.
#[cfg(windows)]
pub fn http_client(config: &Config) -> Result<mbrc_release::WinHttpClient> {
    mbrc_release::WinHttpClient::new(
        &format!("mbrc-plugin/{CORE_VERSION} (+https://github.com/musicbeeremote/mbrc-plugin)"),
        Some(config.proxy_override.as_str()),
    )
}

/// Runs a check according to the user's settings, persisting what it learns.
///
/// `plugin_version` is what `QueryType::PluginVersion` returned - a .NET
/// four-component string, which the comparison normalizes.
///
/// The state file is written on the way out whatever the outcome, including
/// after a failure: that is what makes the backoff work.
///
/// # Errors
/// The release document could not be fetched, or did not parse.
pub fn check_for_update(
    client: &dyn HttpClient,
    config: &Config,
    plugin_version: &str,
    force: bool,
) -> Result<CheckOutcome> {
    if !config.update_check_enabled && !force {
        return Ok(CheckOutcome::Disabled);
    }

    let mut state = load_state(&config.storage_path);
    let options = check_options(config, plugin_version, force);
    let outcome = check::check(client, &options, &mut state, OffsetDateTime::now_utc());

    if let Err(e) = state.save(&config.storage_path) {
        // Not fatal: it costs an early re-check, and losing the check is worse
        // than losing the record of it.
        tracing::warn!(error = %e, "could not persist update state");
    }

    match &outcome {
        Ok(o) => tracing::info!(outcome = ?o, "update check finished"),
        Err(e) => tracing::warn!(error = %e, "update check failed"),
    }
    outcome
}

/// Downloads and stages a verified update under the storage directory. See
/// [`mod@mbrc_release::stage`] for what staging is and is not allowed to touch.
///
/// # Errors
/// The download failed, the bundle did not verify, or the staging directory
/// could not be written.
pub fn stage_update(
    client: &dyn HttpClient,
    config: &Config,
    update: &AvailableUpdate,
) -> Result<StagedUpdate> {
    let staged = stage::stage(
        client,
        update,
        &config.storage_path,
        OffsetDateTime::now_utc(),
    )?;
    tracing::info!(
        version = %staged.version,
        files = staged.files.len(),
        "staged an update for the next restart"
    );
    Ok(staged)
}

/// Removes a staged bundle that has already been applied.
///
/// The helper cannot do it: it runs from inside the bundle, and Windows will not
/// unlink a running image, so its own `clear_staged` fails every time. By the
/// time the core is up again the directory is pure residue.
///
/// The whole `updates/` root goes, since `stage` only clears the version it is
/// about to write. A pending marker means a bundle is still waiting, so it stays.
pub fn sweep_applied_staging(storage_path: &str) {
    if storage_path.is_empty() {
        return;
    }
    match stage::read_pending(storage_path) {
        // Still waiting to be applied: not ours to remove.
        Ok(Some(pending)) => {
            tracing::debug!(version = %pending.version, "leaving a staged update in place");
            return;
        }
        Ok(None) => {}
        Err(e) => {
            // An unreadable marker is not licence to delete what it describes.
            tracing::warn!(error = %e, "not sweeping staged updates: the marker is unreadable");
            return;
        }
    }

    let root = std::path::Path::new(storage_path).join(stage::STAGING_DIR);
    if !root.exists() {
        return;
    }
    match stage::clear_staged(storage_path) {
        Ok(()) => tracing::info!("removed an applied update's staging directory"),
        // Not worth failing anything over: it costs disk, not correctness.
        Err(e) => tracing::warn!(error = %e, "could not remove the staging directory"),
    }
}

/// Records that the user does not want to be offered `version` again.
pub fn skip_version(config: &Config, version: &str) -> Result<()> {
    let mut state = load_state(&config.storage_path);
    state.skipped_version = Some(version.to_owned());
    state.save(&config.storage_path)
}

/// Builds the check inputs from the user's settings.
fn check_options<'a>(config: &Config, plugin_version: &'a str, force: bool) -> CheckOptions<'a> {
    CheckOptions {
        interval_hours: config.update_check_interval_hours,
        force,
        ..CheckOptions::new(plugin_version, config.update_channel)
    }
}

/// Reads the state file, logging (and then ignoring) a corrupt one: a bad state
/// file must not be able to stop the plugin checking for updates ever again.
fn load_state(storage_path: &str) -> UpdateState {
    match UpdateState::load_checked(storage_path) {
        Ok(state) => state,
        Err(e) => {
            tracing::warn!(error = %e, "update state is unreadable; starting fresh");
            UpdateState::default()
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use mbrc_release::Channel;

    #[test]
    fn the_core_version_is_the_product_version() {
        // Not the crate's workspace-internal 0.1.0.
        assert_ne!(CORE_VERSION, "0.1.0");
        assert!(
            CORE_VERSION.split('.').count() >= 2,
            "{CORE_VERSION:?} does not look like a version"
        );
    }

    #[test]
    fn options_come_from_the_settings() {
        let config = Config {
            update_channel: Channel::Testing,
            update_check_interval_hours: 6,
            ..Config::default()
        };
        let options = check_options(&config, "1.5.0.0", false);
        assert_eq!(options.current_version, "1.5.0.0");
        assert_eq!(options.channel, Channel::Testing);
        assert_eq!(options.interval_hours, 6);
        assert!(!options.force);
        assert_eq!(options.repo, check::DEFAULT_REPO);
        // The compiled-in release keys, not an empty or test trust list.
        assert!(!options.keys.is_empty());
    }

    /// A storage dir with a staged bundle in it, optionally with the pending
    /// marker that says it is still waiting to be applied.
    fn staged_fixture(case: &str, pending: bool) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("mbrc-sweep-{case}"));
        let _ = std::fs::remove_dir_all(&dir);
        let version_dir = dir.join("updates").join("1.5.0-beta.2");
        std::fs::create_dir_all(&version_dir).expect("create staging dir");
        // The file the helper cannot delete, because it is running from it.
        std::fs::write(version_dir.join("mbrc-helper.exe"), b"stand-in").expect("seed helper");
        std::fs::write(version_dir.join("mb_remote.dll"), b"stand-in").expect("seed dll");
        if pending {
            std::fs::write(
                dir.join("updates").join("pending.json"),
                br#"{"schema":1,"version":"1.5.0-beta.2","staged_at":"2026-08-22T11:34:26Z"}"#,
            )
            .expect("seed marker");
        }
        dir
    }

    #[test]
    fn an_applied_bundle_is_swept() {
        // No marker: the helper applied it and cleared the marker but could not
        // remove its own directory. This is the leak.
        let dir = staged_fixture("applied", false);
        sweep_applied_staging(dir.to_str().expect("utf8 dir"));
        assert!(
            !dir.join("updates").exists(),
            "the applied bundle should have been removed"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_bundle_still_waiting_is_left_alone() {
        // A marker means it is staged for the next restart - live state, not
        // residue. Sweeping it would silently cancel the user's update.
        let dir = staged_fixture("pending", true);
        sweep_applied_staging(dir.to_str().expect("utf8 dir"));
        assert!(
            dir.join("updates").join("1.5.0-beta.2").exists(),
            "a pending update must survive the sweep"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn sweeping_nothing_is_not_an_error() {
        // A fresh install that has never staged anything, and the no-storage case.
        let dir = std::env::temp_dir().join("mbrc-sweep-empty");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).expect("create scratch dir");
        sweep_applied_staging(dir.to_str().expect("utf8 dir"));
        sweep_applied_staging("");
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_disabled_check_does_not_go_near_the_network() {
        struct NeverCalled;
        impl HttpClient for NeverCalled {
            fn get(&self, _url: &str, _etag: Option<&str>) -> Result<mbrc_release::HttpResponse> {
                panic!("a disabled check must not make a request");
            }
        }

        let config = Config {
            update_check_enabled: false,
            ..Config::default()
        };
        let outcome = check_for_update(&NeverCalled, &config, "1.5.0.0", false).unwrap();
        assert!(matches!(outcome, CheckOutcome::Disabled), "{outcome:?}");
    }
}
