//! Stamps the product version into the helper at compile time.
//!
//! The helper ships in the release bundle next to the two DLLs and is listed in
//! the update manifest, so it has to report the same version as the rest of the
//! product. That version is bumped in exactly one place, `Directory.Build.props`
//! (`<VersionPrefix>`), which the C# assemblies and the CI packaging already
//! read. This reads the same file rather than adding a second place to bump.
//!
//! `MBRC_VERSION` in the environment wins when set: CI builds a full version
//! string that may carry a suffix the props file does not have (nightlies get
//! `-nightly.<date>`), and passes it down.
//!
//! The crate's own `version` in Cargo.toml stays at 0.1.0 like every other crate
//! in this workspace. It is a workspace-internal number and is never published.

use std::path::Path;

fn main() {
    let props = Path::new(env!("CARGO_MANIFEST_DIR")).join("../../Directory.Build.props");
    println!("cargo:rerun-if-env-changed=MBRC_VERSION");
    println!("cargo:rerun-if-changed={}", props.display());

    let version = match std::env::var("MBRC_VERSION") {
        Ok(v) if !v.trim().is_empty() => v.trim().to_string(),
        _ => read_version_prefix(&props),
    };

    println!("cargo:rustc-env=MBRC_VERSION={version}");
}

/// Pulls `<VersionPrefix>x.y.z</VersionPrefix>` out of the props file.
///
/// A missing or malformed file is a hard error rather than a fallback: a helper
/// that silently reports the wrong version would be discovered only after it had
/// shipped in a manifest.
fn read_version_prefix(props: &Path) -> String {
    let xml = std::fs::read_to_string(props)
        .unwrap_or_else(|e| panic!("cannot read {}: {e}", props.display()));

    const OPEN: &str = "<VersionPrefix>";
    const CLOSE: &str = "</VersionPrefix>";

    let start = xml
        .find(OPEN)
        .unwrap_or_else(|| panic!("{OPEN} not found in {}", props.display()))
        + OPEN.len();
    let len = xml[start..]
        .find(CLOSE)
        .unwrap_or_else(|| panic!("{CLOSE} not found in {}", props.display()));

    let version = xml[start..start + len].trim().to_string();
    assert!(
        !version.is_empty(),
        "{OPEN} is empty in {}",
        props.display()
    );
    version
}
