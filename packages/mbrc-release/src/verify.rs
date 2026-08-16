//! Signature and hash verification.
//!
//! Both the core (at download time) and the updater (at apply time) call in
//! here. The apply-time call is not redundant: staged files sit in a directory a
//! non-elevated process can write to, and the updater then copies them into
//! Program Files as an elevated process. Verifying only at download would turn
//! an unprivileged write into an elevated one.

use minisign_verify::{PublicKey, Signature};
use sha2::{Digest, Sha512};

use crate::{
    error::{Result, UpdateError},
    manifest::Manifest,
};

/// A release public key compiled in from `keys/*.pub`.
#[derive(Debug)]
pub struct TrustedKey {
    /// The key's filename stem, for logging which key verified a release.
    pub name: &'static str,
    /// The base64 line from the minisign `.pub` file.
    pub base64: &'static str,
}

include!(concat!(env!("OUT_DIR"), "/trusted_keys.rs"));

/// Verifies a detached minisign signature over `manifest_bytes`, then parses.
///
/// Parsing happens only after the signature checks out, so no unverified input
/// reaches the schema logic. Returns the manifest and the name of the key that
/// verified it.
pub fn verify_manifest(manifest_bytes: &[u8], signature: &str) -> Result<(Manifest, &'static str)> {
    verify_manifest_with(manifest_bytes, signature, TRUSTED_KEYS)
}

/// [`verify_manifest`] against an explicit trust list, for the same reason
/// [`verify_signature_with`] exists: the tests above the network seam drive the
/// whole check against the committed test keypair, and the release keys stay
/// unreachable from a test build.
pub fn verify_manifest_with(
    manifest_bytes: &[u8],
    signature: &str,
    keys: &'static [TrustedKey],
) -> Result<(Manifest, &'static str)> {
    let key_name = verify_signature_with(manifest_bytes, signature, keys)?;
    let manifest = Manifest::parse(manifest_bytes)?;
    Ok((manifest, key_name))
}

/// Verifies a detached signature against every trusted key, returning the name
/// of the first that matches.
///
/// Any trusted key is accepted. Per-key revocation is deliberately not modelled:
/// it would only ever protect installs new enough to have received the
/// revocation, which are the installs least at risk.
pub fn verify_signature(bytes: &[u8], signature: &str) -> Result<&'static str> {
    verify_signature_with(bytes, signature, TRUSTED_KEYS)
}

/// [`verify_signature`] against an explicit trust list.
///
/// Exists so tests can verify against the committed test keypair without the
/// release keys being reachable from a test build, and so the trust list is one
/// visible parameter rather than an ambient global.
pub fn verify_signature_with(
    bytes: &[u8],
    signature: &str,
    keys: &'static [TrustedKey],
) -> Result<&'static str> {
    if keys.is_empty() {
        return Err(UpdateError::NoTrustedKeys);
    }

    let signature =
        Signature::decode(signature).map_err(|e| UpdateError::MalformedSignature(e.to_string()))?;

    // A key that will not parse is skipped, not fatal. One corrupt entry in the
    // compiled-in list would otherwise disable verification against every other
    // key - failing closed, but failing *completely*, and the whole point of a
    // list is that it survives losing one of them. If none parse there is no
    // trust list to speak of, so the first failure is reported.
    let mut malformed: Option<UpdateError> = None;
    let mut usable = 0usize;
    for key in keys {
        let public_key = match PublicKey::from_base64(key.base64) {
            Ok(public_key) => public_key,
            Err(e) => {
                malformed.get_or_insert(UpdateError::MalformedKey {
                    name: key.name.to_owned(),
                    reason: e.to_string(),
                });
                continue;
            }
        };
        usable += 1;

        if public_key.verify(bytes, &signature, false).is_ok() {
            return Ok(key.name);
        }
    }

    if usable == 0 {
        return Err(malformed.unwrap_or(UpdateError::NoTrustedKeys));
    }
    Err(UpdateError::UntrustedSignature)
}

/// Checks `bytes` against a hex-encoded SHA512 from the manifest.
pub fn verify_sha512(bytes: &[u8], expected_hex: &str, path: &str) -> Result<()> {
    let actual = hex::encode(Sha512::digest(bytes));

    // The digest is not a secret and the comparison is not attacker-timeable in
    // any way that matters here, but the manifest signature is the real gate;
    // this only catches corruption and truncated downloads.
    if !actual.eq_ignore_ascii_case(expected_hex) {
        return Err(UpdateError::HashMismatch {
            path: path.to_owned(),
            expected: expected_hex.to_owned(),
            actual,
        });
    }
    Ok(())
}

/// Checks an extracted file against the manifest allowlist and its hash.
///
/// A file absent from `files` is refused outright: the manifest is the complete
/// list of what a bundle may contain.
pub fn verify_bundled_file(manifest: &Manifest, path: &str, bytes: &[u8]) -> Result<()> {
    let expected = manifest
        .expected_hash(path)
        .ok_or_else(|| UpdateError::Invalid(format!("{path:?} is not listed in the manifest")))?;
    verify_sha512(bytes, expected, path)
}

/// The keys this build trusts, for logging and diagnostics.
pub fn trusted_key_names() -> Vec<&'static str> {
    TRUSTED_KEYS.iter().map(|k| k.name).collect()
}
