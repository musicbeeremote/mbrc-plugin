//! Signed release manifest parsing and verification.
//!
//! Shared by the Rust core, which checks and stages updates, and by
//! `mbrc-helper`, which applies them. Both use the same manifest schema and the
//! same trusted keys, so the check performed before download and the one
//! performed before an elevated file copy cannot drift apart.
//!
//! The signing half deliberately lives nowhere in this crate: releases are
//! signed in CI with the `minisign` CLI, and only public keys are compiled in.

pub mod error;
pub mod manifest;
pub mod verify;

pub use error::{Result, UpdateError};
pub use manifest::{Artifact, Artifacts, Channel, FileEntry, Manifest, SCHEMA_VERSION};
pub use verify::{
    verify_bundled_file, verify_manifest, verify_sha512, verify_signature, verify_signature_with,
    TrustedKey, TRUSTED_KEYS,
};
