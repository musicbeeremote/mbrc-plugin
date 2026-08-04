//! Schema and validation tests against the committed golden fixture.
//!
//! The fixture is the same shape CI emits, so a drift between the generator in
//! `.github/actions/package` and this parser fails here rather than during a
//! release.

use mbrc_release::{Channel, Manifest, SCHEMA_VERSION};

const GOLDEN: &str = include_str!("fixtures/manifest.json");

fn golden() -> Manifest {
    Manifest::parse(GOLDEN.as_bytes()).expect("golden fixture must parse")
}

#[test]
fn parses_the_golden_fixture() {
    let m = golden();
    assert_eq!(m.schema, SCHEMA_VERSION);
    assert_eq!(m.channel, Channel::Stable);
    assert_eq!(m.version, "1.5.0");
    assert_eq!(m.abi_version, 3);
    assert_eq!(m.min_musicbee_build, 6500);
    assert_eq!(m.files.len(), 3);
    assert_eq!(m.artifacts.zip.name, "musicbee_remote_1.5.0.zip");
}

#[test]
fn round_trips_through_json() {
    let m = golden();
    let reparsed = Manifest::parse(m.to_json().unwrap().as_bytes()).unwrap();
    assert_eq!(m, reparsed);
}

#[test]
fn expected_hash_is_the_allowlist_lookup() {
    let m = golden();
    assert!(m.expected_hash("mb_remote.dll").is_some());
    assert!(m.expected_hash("mbrc_core.dll").is_some());
    assert!(m.expected_hash("mbrc-helper.exe").is_some());
    // Not in the bundle, so not applyable.
    assert!(m.expected_hash("msvcp140.dll").is_none());
}

#[test]
fn rejects_an_unknown_schema_version() {
    let json = GOLDEN.replace("\"schema\": 1", "\"schema\": 2");
    let err = Manifest::parse(json.as_bytes()).unwrap_err();
    assert!(err.to_string().contains("unsupported schema"), "{err}");
}

#[test]
fn rejects_an_empty_file_list() {
    let json = r#"{
      "schema": 1, "channel": "stable", "version": "1.5.0",
      "released_at": "2026-07-29T12:00:00Z", "abi_version": 3,
      "min_musicbee_build": 6500, "notes_url": "https://example.invalid",
      "artifacts": {
        "zip": {"name": "a.zip", "size": 1, "sha512": "00"},
        "installer": {"name": "a.exe", "size": 1, "sha512": "00"}
      },
      "files": []
    }"#;
    assert!(Manifest::parse(json.as_bytes()).is_err());
}

#[test]
fn rejects_a_truncated_hash() {
    let m = golden();
    let short = &m.files[0].sha512[..64];
    let json = GOLDEN.replace(&m.files[0].sha512, short);
    let err = Manifest::parse(json.as_bytes()).unwrap_err();
    assert!(err.to_string().contains("128 hex characters"), "{err}");
}

#[test]
fn rejects_a_non_hex_hash() {
    let m = golden();
    let bad = "z".repeat(128);
    let json = GOLDEN.replace(&m.files[0].sha512, &bad);
    assert!(Manifest::parse(json.as_bytes()).is_err());
}

#[test]
fn rejects_duplicate_file_entries() {
    let m = golden();
    let json = GOLDEN.replace("\"mbrc_core.dll\"", "\"mb_remote.dll\"");
    let err = Manifest::parse(json.as_bytes()).unwrap_err();
    assert!(err.to_string().contains("duplicate"), "{err}");
    drop(m);
}

/// The zip-slip guard. Each of these escapes the plugins directory, and the
/// updater writes as an elevated process, so they are refused at parse time
/// rather than left for the extractor to remember.
#[test]
fn rejects_paths_that_are_not_bare_filenames() {
    for evil in [
        "../mb_remote.dll",
        "..\\mb_remote.dll",
        "sub/mb_remote.dll",
        "sub\\mb_remote.dll",
        "C:\\Windows\\System32\\evil.dll",
        "/etc/passwd",
        "..",
        ".",
        ".hidden",
        "",
    ] {
        let json = GOLDEN.replace("\"mb_remote.dll\"", &format!("{evil:?}"));
        assert!(
            Manifest::parse(json.as_bytes()).is_err(),
            "{evil:?} should be rejected as a file path"
        );
    }
}

/// Names that stay inside the directory but still do not mean what they look
/// like on Windows, where these files are written.
#[test]
fn rejects_windows_traps_that_are_not_path_traversal() {
    for evil in [
        "nul",              // a device, in every directory
        "NUL.dll",          // still a device: the extension is ignored
        "com1.dll",         //
        "mb_remote.dll.",   // the trailing dot is stripped, so this is the same file
        "mb_remote.dll ",   // as is the trailing space
        "mb_remote:stream", // an alternate data stream on the real file
        "mb_remote*.dll",   // a wildcard, refused by the filesystem anyway
        "mb\u{0}remote.dll",
    ] {
        let json = GOLDEN.replace("\"mb_remote.dll\"", &format!("{evil:?}"));
        assert!(
            Manifest::parse(json.as_bytes()).is_err(),
            "{evil:?} should be rejected as a file path"
        );
    }
}

#[test]
fn rejects_artifact_names_that_are_not_bare_filenames() {
    let json = GOLDEN.replace(
        "\"musicbee_remote_1.5.0.zip\"",
        "\"../../musicbee_remote_1.5.0.zip\"",
    );
    assert!(Manifest::parse(json.as_bytes()).is_err());
}

#[test]
fn rejects_malformed_json() {
    assert!(Manifest::parse(b"not json").is_err());
    assert!(Manifest::parse(b"{}").is_err());
}
