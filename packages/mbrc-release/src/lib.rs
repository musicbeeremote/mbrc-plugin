//! Release manifests, and the update check that acts on them.
//!
//! Shared by the Rust core, which checks and stages updates, and by
//! `mbrc-helper`, which applies them. Both use the same manifest schema and the
//! same trusted keys, so the check performed before download and the one
//! performed before an elevated file copy cannot drift apart.
//!
//! No signing lives here: releases are signed in CI with the `minisign` CLI and
//! only public keys are compiled in.
//!
//! # Layout
//!
//! - [`manifest`] / [`verify`] - the schema and its signature. Always compiled.
//! - [`mod@check`] / [`mod@stage`] / [`state`] / [`http`] / [`version`] - finding an
//!   update and putting it where the helper can find it. Behind the `client`
//!   feature, off for the helper so an elevated process carries less.
//! - `winhttp` - the one production `HttpClient`, and the only Windows-bound
//!   thing here.
//!
//! The client half sits above the [`http::HttpClient`] trait, so it is all
//! tested against a stub on any host.

pub mod error;
pub mod manifest;
pub mod verify;

#[cfg(feature = "client")]
pub mod check;
#[cfg(feature = "client")]
pub mod http;
#[cfg(feature = "client")]
pub mod stage;
#[cfg(feature = "client")]
pub mod state;
#[cfg(feature = "client")]
pub mod version;
#[cfg(all(feature = "client", windows))]
pub mod winhttp;

pub use error::{Result, UpdateError};
pub use manifest::{
    Artifact, Artifacts, Channel, FileEntry, Manifest, SCHEMA_VERSION, is_bare_filename,
};
pub use verify::{
    TRUSTED_KEYS, TrustedKey, verify_bundled_file, verify_manifest, verify_manifest_with,
    verify_sha512, verify_signature, verify_signature_with,
};

#[cfg(feature = "client")]
pub use check::{AvailableUpdate, CheckOptions, CheckOutcome, check};
#[cfg(feature = "client")]
pub use http::{HttpClient, HttpResponse};
#[cfg(feature = "client")]
pub use stage::{Pending, StagedUpdate, clear_staged, read_pending, stage};
#[cfg(feature = "client")]
pub use state::UpdateState;
#[cfg(all(feature = "client", windows))]
pub use winhttp::WinHttpClient;
