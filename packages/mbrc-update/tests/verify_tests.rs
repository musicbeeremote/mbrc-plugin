//! Signature and hash verification tests.
//!
//! Signatures are checked against the committed unencrypted test keypair in
//! `tests/keys/`, never the release keys. Regenerate the fixture signature with:
//!
//! ```text
//! cd packages/mbrc-update/tests/fixtures
//! minisign -S -s ../keys/test.key -m manifest.json -t "mbrc test manifest"
//! ```

use mbrc_update::{
    verify_bundled_file, verify_sha512, verify_signature_with, Manifest, TrustedKey, TRUSTED_KEYS,
};

const GOLDEN: &str = include_str!("fixtures/manifest.json");
const GOLDEN_SIG: &str = include_str!("fixtures/manifest.json.minisig");

/// The test key's base64 line, lifted from `tests/keys/test.pub`.
const TEST_KEYS: &[TrustedKey] = &[TrustedKey {
    name: "test",
    base64: "RWT+ztjSHP1aBowOy75aVsw0jf2Vn6MMbzuTIAPRaN5EWVPjPU9fjwAj",
}];

#[test]
fn verifies_the_golden_signature() {
    let name = verify_signature_with(GOLDEN.as_bytes(), GOLDEN_SIG, TEST_KEYS)
        .expect("golden fixture signature must verify");
    assert_eq!(name, "test");
}

#[test]
fn rejects_a_tampered_manifest() {
    // A single character: the version the updater would compare against.
    let tampered = GOLDEN.replace("\"version\": \"1.5.0\"", "\"version\": \"9.9.9\"");
    assert_ne!(tampered, GOLDEN, "tamper must actually change the bytes");
    assert!(verify_signature_with(tampered.as_bytes(), GOLDEN_SIG, TEST_KEYS).is_err());
}

#[test]
fn rejects_a_tampered_hash() {
    let m = Manifest::parse(GOLDEN.as_bytes()).unwrap();
    let tampered = GOLDEN.replace(&m.files[0].sha512, &"a".repeat(128));
    assert!(verify_signature_with(tampered.as_bytes(), GOLDEN_SIG, TEST_KEYS).is_err());
}

#[test]
fn rejects_a_signature_from_an_untrusted_key() {
    // The real release keys did not sign the test fixture.
    assert!(verify_signature_with(GOLDEN.as_bytes(), GOLDEN_SIG, TRUSTED_KEYS).is_err());
}

#[test]
fn rejects_a_malformed_signature() {
    assert!(verify_signature_with(GOLDEN.as_bytes(), "not a signature", TEST_KEYS).is_err());
    assert!(verify_signature_with(GOLDEN.as_bytes(), "", TEST_KEYS).is_err());
}

#[test]
fn fails_closed_with_no_trusted_keys() {
    let err = verify_signature_with(GOLDEN.as_bytes(), GOLDEN_SIG, &[]).unwrap_err();
    assert!(err.to_string().contains("no release public keys"), "{err}");
}

#[test]
fn sha512_accepts_a_matching_digest() {
    let m = Manifest::parse(GOLDEN.as_bytes()).unwrap();
    // The fixture's hashes are over these exact strings; see the generator note
    // in the test module docs.
    verify_sha512(
        b"mb_remote.dll bytes",
        m.expected_hash("mb_remote.dll").unwrap(),
        "mb_remote.dll",
    )
    .expect("matching digest must verify");
}

#[test]
fn sha512_rejects_a_mismatched_digest() {
    let m = Manifest::parse(GOLDEN.as_bytes()).unwrap();
    let err = verify_sha512(
        b"mb_remote.dll bytes tampered",
        m.expected_hash("mb_remote.dll").unwrap(),
        "mb_remote.dll",
    )
    .unwrap_err();
    assert!(err.to_string().contains("sha512 mismatch"), "{err}");
}

#[test]
fn sha512_is_case_insensitive() {
    let m = Manifest::parse(GOLDEN.as_bytes()).unwrap();
    let upper = m.expected_hash("mb_remote.dll").unwrap().to_uppercase();
    verify_sha512(b"mb_remote.dll bytes", &upper, "mb_remote.dll").unwrap();
}

#[test]
fn bundled_file_must_be_in_the_allowlist() {
    let m = Manifest::parse(GOLDEN.as_bytes()).unwrap();

    verify_bundled_file(&m, "mb_remote.dll", b"mb_remote.dll bytes").unwrap();

    // Correct-looking file that the manifest never listed: refused outright,
    // because the manifest is the complete list of what a bundle may contain.
    let err = verify_bundled_file(&m, "evil.dll", b"whatever").unwrap_err();
    assert!(err.to_string().contains("not listed"), "{err}");
}

/// Guards the real trust list itself: a mangled or truncated `.pub` file should
/// fail here, at test time, not during a release.
#[test]
fn compiled_in_release_keys_are_well_formed() {
    assert!(
        !TRUSTED_KEYS.is_empty(),
        "no release keys compiled in; see packages/mbrc-update/keys/README.md"
    );

    for key in TRUSTED_KEYS {
        // Signature verification against arbitrary bytes must fail, but it must
        // fail as UntrustedSignature, not MalformedKey.
        let err = verify_signature_with(b"unsigned", GOLDEN_SIG, std::slice::from_ref(key))
            .unwrap_err()
            .to_string();
        assert!(
            !err.contains("malformed"),
            "release key {} is malformed: {err}",
            key.name
        );
    }
}
