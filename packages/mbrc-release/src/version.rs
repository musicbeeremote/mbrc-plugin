//! Turning the product's version strings into comparable semver.
//!
//! The version reaches the core as a .NET `System.Version.ToString()`, which is
//! four components (`"1.5.0.0"`), while the manifest carries three (`"1.5.0"`).
//! `semver` parses neither interchangeably: it rejects the four-component form
//! outright. Everything therefore goes through [`parse`] before it is compared.
//!
//! Only major.minor.patch has ever been meaningful in this project - the fourth
//! component is an artefact of the .NET type and is always zero in a release - so
//! normalizing is dropping it, not losing information.

use semver::Version;

use crate::error::{Result, UpdateError};

/// Parses a product version into comparable semver.
///
/// Accepts what the producers emit: four components from .NET (the revision is
/// dropped), fewer than three (the rest are zero), an optional `v` prefix as
/// the git tags carry, and a prerelease or build suffix kept intact so ordering
/// stays semver's job.
///
/// # Errors
/// The string is not a version this can compare.
pub fn parse(raw: &str) -> Result<Version> {
    let trimmed = raw.trim();
    let trimmed = trimmed.strip_prefix(['v', 'V']).unwrap_or(trimmed);
    if trimmed.is_empty() {
        return Err(UpdateError::Version("version is empty".into()));
    }

    // Split the numeric core off any prerelease/build suffix before counting
    // components, so the `.` inside `-nightly.20260804` is not mistaken for one.
    let split = trimmed.find(['-', '+']).unwrap_or(trimmed.len());
    let (core, suffix) = trimmed.split_at(split);

    let mut parts = core.split('.');
    let mut numbers = [0u64; 3];
    for (index, slot) in numbers.iter_mut().enumerate() {
        let Some(part) = parts.next() else { break };
        *slot = part.parse().map_err(|_| {
            UpdateError::Version(format!(
                "{raw:?}: component {} is not a number ({part:?})",
                index + 1
            ))
        })?;
    }

    // A fourth component is the .NET revision and is dropped, but it still has to
    // look like one: a trailing `.beta` would otherwise vanish silently.
    if let Some(revision) = parts.next() {
        revision.parse::<u64>().map_err(|_| {
            UpdateError::Version(format!("{raw:?}: revision is not a number ({revision:?})"))
        })?;
    }
    if parts.next().is_some() {
        return Err(UpdateError::Version(format!(
            "{raw:?}: more than four components"
        )));
    }

    let [major, minor, patch] = numbers;
    Version::parse(&format!("{major}.{minor}.{patch}{suffix}"))
        .map_err(|e| UpdateError::Version(format!("{raw:?}: {e}")))
}

#[cfg(test)]
mod tests {
    use super::*;

    fn v(s: &str) -> Version {
        parse(s).unwrap_or_else(|e| panic!("{s:?} should parse: {e}"))
    }

    #[test]
    fn drops_the_dotnet_revision() {
        // What QueryType::PluginVersion actually returns.
        assert_eq!(v("1.5.0.0"), Version::new(1, 5, 0));
        assert_eq!(v("1.5.0.7"), Version::new(1, 5, 0));
    }

    #[test]
    fn accepts_the_manifest_and_tag_forms() {
        assert_eq!(v("1.5.0"), Version::new(1, 5, 0));
        assert_eq!(v("v1.5.0"), Version::new(1, 5, 0));
        assert_eq!(v(" 1.5.0 "), Version::new(1, 5, 0));
    }

    #[test]
    fn pads_missing_components() {
        assert_eq!(v("1.5"), Version::new(1, 5, 0));
        assert_eq!(v("2"), Version::new(2, 0, 0));
    }

    #[test]
    fn keeps_prerelease_and_build_suffixes() {
        assert_eq!(
            v("1.6.0-nightly.20260804").to_string(),
            "1.6.0-nightly.20260804"
        );
        // The two forms of the same nightly must compare equal, which is the
        // whole point of normalizing before comparing.
        assert_eq!(v("1.6.0.0-nightly.1"), v("1.6.0-nightly.1"));
        assert_eq!(v("1.6.0+build.5").to_string(), "1.6.0+build.5");
    }

    #[test]
    fn a_prerelease_orders_below_its_release() {
        // Semver's rule, and the reason nightly ordering is left to it: a
        // nightly is older than the stable release of the same number.
        assert!(v("1.6.0-nightly.20260804") < v("1.6.0"));
        assert!(v("1.5.0") < v("1.6.0-nightly.20260804"));
    }

    #[test]
    fn rejects_what_is_not_a_version() {
        assert!(parse("").is_err());
        assert!(parse("   ").is_err());
        assert!(parse("abc").is_err());
        assert!(parse("1.x.0").is_err());
        assert!(parse("1.5.0.beta").is_err());
        assert!(parse("1.2.3.4.5").is_err());
    }
}
