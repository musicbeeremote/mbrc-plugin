//! Stamps the product version into a crate at compile time.
//!
//! The version is bumped in exactly one place, `Directory.Build.props`
//! (`<VersionPrefix>`), which the C# assemblies and the CI packaging already
//! read. Every Rust crate that has to report the product's version reads that
//! same file through here, so there is never a second place to bump.
//!
//! A build script calls [`emit_version`], and the crate reads the result back
//! with `env!("MBRC_VERSION")`.
//!
//! Crate `version` fields in this workspace all stay at 0.1.0. They are
//! workspace-internal numbers and nothing publishes them.

use std::path::{Path, PathBuf};

/// The props file every version comes from.
pub const PROPS_FILE: &str = "Directory.Build.props";

/// Emits `MBRC_VERSION` for the calling crate, plus the rerun triggers that keep
/// it current.
///
/// `MBRC_VERSION` in the environment wins when set: CI builds a full version
/// string that may carry a suffix the props file does not have (nightlies get
/// `-nightly.<date>`) and passes it down.
pub fn emit_version() {
    println!("cargo:rerun-if-env-changed=MBRC_VERSION");

    let version = match std::env::var("MBRC_VERSION") {
        Ok(v) if !v.trim().is_empty() => v.trim().to_owned(),
        _ => {
            let props = find_props();
            println!("cargo:rerun-if-changed={}", props.display());
            let xml = std::fs::read_to_string(&props)
                .unwrap_or_else(|e| panic!("cannot read {}: {e}", props.display()));
            version_prefix(&xml)
                .unwrap_or_else(|e| panic!("{}: {e}", props.display()))
                .to_owned()
        }
    };

    println!("cargo:rustc-env=MBRC_VERSION={version}");
}

/// Walks up from the calling crate until it finds the props file.
///
/// Searching rather than hard-coding `../..` so moving a crate one level deeper
/// is not a silent breakage in a build script nobody reads.
fn find_props() -> PathBuf {
    let manifest_dir =
        std::env::var("CARGO_MANIFEST_DIR").expect("CARGO_MANIFEST_DIR is set by cargo");
    let start = Path::new(&manifest_dir);
    for dir in start.ancestors() {
        let candidate = dir.join(PROPS_FILE);
        if candidate.is_file() {
            return candidate;
        }
    }
    panic!("{PROPS_FILE} not found above {}", start.display());
}

/// Pulls `<VersionPrefix>x.y.z</VersionPrefix>` out of the props file.
///
/// A missing or empty element is a hard error rather than a fallback: a build
/// that silently reports the wrong version is discovered only after it has
/// shipped in a release manifest.
fn version_prefix(xml: &str) -> std::result::Result<&str, String> {
    const OPEN: &str = "<VersionPrefix>";
    const CLOSE: &str = "</VersionPrefix>";

    let start = xml.find(OPEN).ok_or_else(|| format!("{OPEN} not found"))? + OPEN.len();
    let len = xml[start..]
        .find(CLOSE)
        .ok_or_else(|| format!("{CLOSE} not found"))?;

    let version = xml[start..start + len].trim();
    if version.is_empty() {
        return Err(format!("{OPEN} is empty"));
    }
    Ok(version)
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_the_version_prefix() {
        let xml = "<Project>\n  <PropertyGroup>\n    <VersionPrefix>1.5.0</VersionPrefix>\n  </PropertyGroup>\n</Project>";
        assert_eq!(version_prefix(xml).unwrap(), "1.5.0");
    }

    #[test]
    fn rejects_a_missing_or_empty_element() {
        assert!(version_prefix("<Project/>").is_err());
        assert!(version_prefix("<VersionPrefix></VersionPrefix>").is_err());
        assert!(version_prefix("<VersionPrefix>   </VersionPrefix>").is_err());
        assert!(version_prefix("<VersionPrefix>1.5.0").is_err());
    }

    /// The real file, so a malformed props file fails here rather than in a
    /// build script's panic during a release.
    #[test]
    fn the_repository_props_file_carries_a_version() {
        let props = find_props();
        let xml = std::fs::read_to_string(&props).unwrap();
        let version = version_prefix(&xml).unwrap();
        assert!(
            version.split('.').count() >= 2,
            "{version:?} does not look like a version"
        );
    }
}
