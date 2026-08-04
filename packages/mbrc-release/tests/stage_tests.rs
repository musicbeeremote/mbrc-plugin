//! Staging: what gets written, where, and what refuses to be written at all.
//!
//! These manifests are built in the test rather than signed, because staging's
//! contract starts *after* verification: `check` proves the manifest came from a
//! release key, and `stage` is what happens next. The signature bytes here are
//! opaque - staging copies them to disk for the helper to re-verify, and never
//! looks at them.

mod support;

use std::io::Write;
use std::path::Path;

use mbrc_release::{
    clear_staged,
    manifest::{Artifact, Artifacts, FileEntry},
    read_pending, stage,
    stage::{PENDING_FILE, PENDING_SCHEMA, STAGING_DIR},
    AvailableUpdate, Channel, Manifest, UpdateError,
};
use sha2::{Digest, Sha512};
use support::StubHttp;
use time::{format_description::well_known::Rfc3339, OffsetDateTime};

const ZIP_URL: &str = "https://assets.test/musicbee_remote_1.6.0.zip";
const NOW: &str = "2026-08-04T10:00:00Z";

fn now() -> OffsetDateTime {
    OffsetDateTime::parse(NOW, &Rfc3339).unwrap()
}

fn sha512(bytes: &[u8]) -> String {
    hex::encode(Sha512::digest(bytes))
}

fn temp_dir(name: &str) -> String {
    let dir = std::env::temp_dir().join(format!("mbrc-stage-{name}"));
    let _ = std::fs::remove_dir_all(&dir);
    std::fs::create_dir_all(&dir).unwrap();
    dir.to_string_lossy().into_owned()
}

/// A zip with stored (uncompressed) entries - the reader path is what is under
/// test, not the compressor.
fn build_zip(entries: &[(&str, &[u8])]) -> Vec<u8> {
    let mut writer = zip::ZipWriter::new(std::io::Cursor::new(Vec::new()));
    let options: zip::write::FileOptions<'_, ()> =
        zip::write::FileOptions::default().compression_method(zip::CompressionMethod::Stored);
    for (name, bytes) in entries {
        writer.start_file(*name, options).unwrap();
        writer.write_all(bytes).unwrap();
    }
    writer.finish().unwrap().into_inner()
}

fn manifest_for(files: &[(&str, &[u8])], zip_bytes: &[u8]) -> Manifest {
    Manifest {
        schema: 1,
        channel: Channel::Stable,
        version: "1.6.0".into(),
        released_at: NOW.into(),
        abi_version: 3,
        min_musicbee_build: 6500,
        notes_url: "https://example.test/notes".into(),
        artifacts: Artifacts {
            zip: Artifact {
                name: "musicbee_remote_1.6.0.zip".into(),
                size: zip_bytes.len() as u64,
                sha512: sha512(zip_bytes),
            },
            installer: Artifact {
                name: "musicbee_remote_1.6.0.exe".into(),
                size: 1,
                sha512: sha512(b"installer"),
            },
        },
        files: files
            .iter()
            .map(|(path, bytes)| FileEntry {
                path: (*path).into(),
                sha512: sha512(bytes),
            })
            .collect(),
    }
}

fn available(manifest: Manifest) -> AvailableUpdate {
    AvailableUpdate {
        manifest_bytes: manifest.to_json().unwrap().into_bytes(),
        manifest,
        signature: "untrusted comment: test\nRWTest\n".into(),
        zip_url: ZIP_URL.into(),
        key_name: "test",
    }
}

/// The happy path: an archive whose listed files all verify.
fn good_bundle() -> (StubHttp, AvailableUpdate) {
    let payload: &[(&str, &[u8])] = &[
        ("mb_remote.dll", b"managed shim bytes"),
        ("mbrc_core.dll", b"native core bytes"),
        ("mbrc-helper.exe", b"helper bytes"),
    ];
    // LICENSE rides along in the real zip and is not listed in the manifest.
    let mut entries = payload.to_vec();
    entries.push(("LICENSE", b"license text"));
    let zip = build_zip(&entries);

    let stub = StubHttp::default();
    stub.serve(ZIP_URL, &zip);
    (stub, available(manifest_for(payload, &zip)))
}

#[test]
fn stages_every_listed_file_and_the_marker_last() {
    let dir = temp_dir("happy");
    let (stub, update) = good_bundle();

    let staged = stage(&stub, &update, &dir, now()).unwrap();

    assert_eq!(staged.version, "1.6.0");
    assert_eq!(staged.dir, Path::new(&dir).join(STAGING_DIR).join("1.6.0"));
    assert_eq!(
        staged.files,
        vec!["mb_remote.dll", "mbrc_core.dll", "mbrc-helper.exe"]
    );

    assert_eq!(
        std::fs::read(staged.dir.join("mb_remote.dll")).unwrap(),
        b"managed shim bytes"
    );
    // The signed manifest and its signature are staged alongside, so the
    // elevated apply re-verifies from these rather than trusting the marker.
    assert_eq!(
        std::fs::read(staged.dir.join("manifest.json")).unwrap(),
        update.manifest_bytes
    );
    assert_eq!(
        std::fs::read_to_string(staged.dir.join("manifest.json.minisig")).unwrap(),
        update.signature
    );
    // An unlisted entry is simply never extracted.
    assert!(!staged.dir.join("LICENSE").exists());

    let pending = read_pending(&dir).unwrap().expect("a marker");
    assert_eq!(pending.schema, PENDING_SCHEMA);
    assert_eq!(pending.version, "1.6.0");
    assert_eq!(pending.staged_at, NOW);
    // The marker names a version, never a path: the helper joins it to a
    // storage directory it derives itself.
    let raw =
        std::fs::read_to_string(Path::new(&dir).join(STAGING_DIR).join(PENDING_FILE)).unwrap();
    assert!(!raw.contains(&dir.replace('\\', "\\\\")), "{raw}");
}

#[test]
fn an_entry_that_is_not_a_bare_filename_refuses_the_whole_bundle() {
    for evil in [
        "../evil.dll",
        "sub/dir.dll",
        "C:\\windows\\system32\\evil.dll",
    ] {
        let dir = temp_dir("unsafe-entry");
        let payload: &[(&str, &[u8])] = &[("mb_remote.dll", b"managed shim bytes")];
        let mut entries = payload.to_vec();
        entries.push((evil, b"evil"));
        let zip = build_zip(&entries);

        let stub = StubHttp::default();
        stub.serve(ZIP_URL, &zip);
        let update = available(manifest_for(payload, &zip));

        let error = stage(&stub, &update, &dir, now()).unwrap_err();
        assert!(
            matches!(error, UpdateError::UnsafeEntry(_)),
            "{evil}: {error:?}"
        );
        // Refused before anything was written, marker included.
        assert!(!Path::new(&dir).join(STAGING_DIR).join("1.6.0").exists());
        assert!(read_pending(&dir).unwrap().is_none());
    }
}

#[test]
fn a_file_whose_hash_is_wrong_stages_nothing() {
    let dir = temp_dir("hash-mismatch");
    let payload: &[(&str, &[u8])] = &[
        ("mb_remote.dll", b"managed shim bytes"),
        ("mbrc_core.dll", b"native core bytes"),
    ];
    let zip = build_zip(&[
        ("mb_remote.dll", b"managed shim bytes"),
        ("mbrc_core.dll", b"native core bytes, swapped"),
    ]);
    let stub = StubHttp::default();
    stub.serve(ZIP_URL, &zip);
    let update = available(manifest_for(payload, &zip));

    let error = stage(&stub, &update, &dir, now()).unwrap_err();
    assert!(
        matches!(error, UpdateError::HashMismatch { .. }),
        "{error:?}"
    );
    // Not even the file that did verify: the bundle is applied whole or not at
    // all, so a partial directory would only be something to clean up later.
    assert!(!Path::new(&dir).join(STAGING_DIR).join("1.6.0").exists());
    assert!(read_pending(&dir).unwrap().is_none());
}

#[test]
fn a_manifest_listing_a_file_the_zip_lacks_stages_nothing() {
    let dir = temp_dir("missing-entry");
    let payload: &[(&str, &[u8])] = &[("mb_remote.dll", b"managed shim bytes")];
    let zip = build_zip(&[("something_else.dll", b"whatever")]);
    let stub = StubHttp::default();
    stub.serve(ZIP_URL, &zip);
    let update = available(manifest_for(payload, &zip));

    let error = stage(&stub, &update, &dir, now()).unwrap_err();
    assert!(matches!(error, UpdateError::MissingEntry(_)), "{error:?}");
    assert!(read_pending(&dir).unwrap().is_none());
}

#[test]
fn a_truncated_download_is_refused_before_it_is_opened() {
    let dir = temp_dir("truncated");
    let (_, update) = good_bundle();
    let stub = StubHttp::default();
    stub.serve(ZIP_URL, b"short");

    let error = stage(&stub, &update, &dir, now()).unwrap_err();
    assert!(
        matches!(&error, UpdateError::Invalid(m) if m.contains("bytes")),
        "{error:?}"
    );
}

#[test]
fn a_zip_that_is_not_a_zip_is_an_archive_error() {
    let dir = temp_dir("not-a-zip");
    let bytes = b"this is not a zip file".to_vec();
    let mut manifest = manifest_for(&[("mb_remote.dll", b"x")], &bytes);
    manifest.artifacts.zip.size = bytes.len() as u64;
    manifest.artifacts.zip.sha512 = sha512(&bytes);
    let stub = StubHttp::default();
    stub.serve(ZIP_URL, &bytes);

    let error = stage(&stub, &available(manifest), &dir, now()).unwrap_err();
    assert!(matches!(error, UpdateError::Archive(_)), "{error:?}");
}

#[test]
fn restaging_replaces_a_previous_attempt() {
    let dir = temp_dir("restage");
    let (stub, update) = good_bundle();
    let staged = stage(&stub, &update, &dir, now()).unwrap();

    // Something left over from an earlier attempt at the same version.
    std::fs::write(staged.dir.join("stale.dll"), b"stale").unwrap();
    let staged = stage(&stub, &update, &dir, now()).unwrap();
    assert!(!staged.dir.join("stale.dll").exists());
    assert!(staged.dir.join("mb_remote.dll").exists());
}

#[test]
fn clearing_removes_the_staging_tree() {
    let dir = temp_dir("clear");
    let (stub, update) = good_bundle();
    stage(&stub, &update, &dir, now()).unwrap();

    clear_staged(&dir).unwrap();
    assert!(read_pending(&dir).unwrap().is_none());
    assert!(!Path::new(&dir).join(STAGING_DIR).exists());
    // Clearing what is not there is not an error.
    clear_staged(&dir).unwrap();
}

#[test]
fn a_marker_that_names_a_path_or_an_unknown_schema_is_refused() {
    let dir = temp_dir("bad-marker");
    let root = Path::new(&dir).join(STAGING_DIR);
    std::fs::create_dir_all(&root).unwrap();

    // A version that would escape the staging root if it were joined blindly.
    // The helper re-checks this too (#151); this is the first of the two.
    std::fs::write(
        root.join(PENDING_FILE),
        r#"{"schema":1,"version":"../../plugins","staged_at":"","files":[]}"#,
    )
    .unwrap();
    assert!(matches!(
        read_pending(&dir).unwrap_err(),
        UpdateError::Invalid(_)
    ));

    std::fs::write(
        root.join(PENDING_FILE),
        r#"{"schema":99,"version":"1.6.0","staged_at":"","files":[]}"#,
    )
    .unwrap();
    assert!(matches!(
        read_pending(&dir).unwrap_err(),
        UpdateError::Invalid(_)
    ));

    // Corrupt is an error rather than "nothing staged": something wrote it.
    std::fs::write(root.join(PENDING_FILE), "{ not json").unwrap();
    assert!(matches!(
        read_pending(&dir).unwrap_err(),
        UpdateError::Parse(_)
    ));
}

#[test]
fn a_version_that_is_not_a_bare_filename_is_never_a_directory() {
    let dir = temp_dir("bad-version");
    let (stub, mut update) = good_bundle();
    update.manifest.version = "../../../plugins".into();

    let error = stage(&stub, &update, &dir, now()).unwrap_err();
    assert!(
        matches!(&error, UpdateError::Invalid(m) if m.contains("directory name")),
        "{error:?}"
    );
}
