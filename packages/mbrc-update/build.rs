//! Compiles the trusted release public keys into the verifier.
//!
//! Every `keys/*.pub` is read at build time and emitted as a `TRUSTED_KEYS`
//! table, so adding or rotating a key is a file drop rather than a code edit.
//! Only the base64 line is kept; minisign's `untrusted comment:` header is, as
//! the name says, not authenticated and is discarded.

use std::{env, fs, path::PathBuf};

fn main() {
    let keys_dir = PathBuf::from(env!("CARGO_MANIFEST_DIR")).join("keys");
    println!("cargo:rerun-if-changed={}", keys_dir.display());

    let mut keys: Vec<(String, String)> = Vec::new();

    let entries = fs::read_dir(&keys_dir)
        .unwrap_or_else(|e| panic!("cannot read {}: {e}", keys_dir.display()));

    for entry in entries {
        let path = entry.expect("cannot read key directory entry").path();
        if path.extension().and_then(|e| e.to_str()) != Some("pub") {
            continue;
        }
        println!("cargo:rerun-if-changed={}", path.display());

        let name = path
            .file_stem()
            .and_then(|s| s.to_str())
            .expect("key filename is not valid UTF-8")
            .to_owned();
        let contents = fs::read_to_string(&path)
            .unwrap_or_else(|e| panic!("cannot read {}: {e}", path.display()));

        let base64 = contents
            .lines()
            .map(str::trim)
            .find(|line| !line.is_empty() && !line.starts_with("untrusted comment:"))
            .unwrap_or_else(|| panic!("{} has no key line", path.display()));

        keys.push((name, base64.to_owned()));
    }

    // Sorted so the trust-list order is deterministic across machines and does
    // not depend on directory iteration order.
    keys.sort();

    if keys.is_empty() {
        // Not fatal: the crate must still build in a fresh checkout before the
        // keys are dropped in. Verification fails closed at runtime instead.
        println!("cargo:warning=no release public keys in {} — signature verification will reject every manifest", keys_dir.display());
    }

    let mut out = String::from("pub const TRUSTED_KEYS: &[TrustedKey] = &[\n");
    for (name, base64) in &keys {
        out.push_str(&format!(
            "    TrustedKey {{ name: {name:?}, base64: {base64:?} }},\n"
        ));
    }
    out.push_str("];\n");

    let dest = PathBuf::from(env::var("OUT_DIR").expect("OUT_DIR not set")).join("trusted_keys.rs");
    fs::write(&dest, out).unwrap_or_else(|e| panic!("cannot write {}: {e}", dest.display()));
}
