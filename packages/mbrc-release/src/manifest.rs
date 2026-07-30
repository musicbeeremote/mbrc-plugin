//! The signed release manifest.
//!
//! One manifest per release, signed as a whole, so a single signature covers the
//! installer, the zip, and every file inside it. See `docs/updates.md`.

use serde::{Deserialize, Serialize};

use crate::error::{Result, UpdateError};

/// The only schema version this build understands. A manifest declaring anything
/// else is rejected rather than best-effort parsed: an updater that guesses at a
/// format it does not know is an updater that installs the wrong bytes.
pub const SCHEMA_VERSION: u32 = 1;

/// Length of a hex-encoded SHA512 digest.
const SHA512_HEX_LEN: usize = 128;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[serde(rename_all = "lowercase")]
pub enum Channel {
    Stable,
    Nightly,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Artifact {
    /// Filename as published in the release assets. Never a path.
    pub name: String,
    pub size: u64,
    pub sha512: String,
}

/// One file inside the zip, and the hash it must have once extracted.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct FileEntry {
    /// A bare filename, relative to the plugins directory.
    pub path: String,
    pub sha512: String,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Artifacts {
    pub zip: Artifact,
    pub installer: Artifact,
}

#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Manifest {
    pub schema: u32,
    pub channel: Channel,
    pub version: String,
    pub released_at: String,
    /// `MBRC_ABI_VERSION` the bundle was built against, so a structurally
    /// unrunnable update is rejected before it is downloaded rather than after.
    pub abi_version: u32,
    pub min_musicbee_build: u32,
    pub notes_url: String,
    pub artifacts: Artifacts,
    /// Every file the zip is allowed to contain. This is the extraction
    /// allowlist, not a description: entries outside it are refused.
    pub files: Vec<FileEntry>,
}

impl Manifest {
    /// Parses and validates. Prefer [`crate::verify::verify_manifest`], which
    /// checks the signature first: parsing unverified bytes is only safe because
    /// nothing here acts on them.
    pub fn parse(bytes: &[u8]) -> Result<Self> {
        let manifest: Self =
            serde_json::from_slice(bytes).map_err(|e| UpdateError::Parse(e.to_string()))?;
        manifest.validate()?;
        Ok(manifest)
    }

    pub fn to_json(&self) -> Result<String> {
        serde_json::to_string_pretty(self).map_err(|e| UpdateError::Parse(e.to_string()))
    }

    /// Looks up the expected hash for an extracted file. `None` means the file is
    /// not in the allowlist and must not be written.
    pub fn expected_hash(&self, path: &str) -> Option<&str> {
        self.files
            .iter()
            .find(|f| f.path == path)
            .map(|f| f.sha512.as_str())
    }

    fn validate(&self) -> Result<()> {
        if self.schema != SCHEMA_VERSION {
            return Err(UpdateError::Invalid(format!(
                "unsupported schema {}, this build understands {SCHEMA_VERSION}",
                self.schema
            )));
        }
        if self.version.trim().is_empty() {
            return Err(UpdateError::Invalid("version is empty".into()));
        }
        if self.files.is_empty() {
            return Err(UpdateError::Invalid(
                "files is empty, so the zip would have no allowed entries".into(),
            ));
        }

        check_hash(&self.artifacts.zip.sha512, "artifacts.zip")?;
        check_hash(&self.artifacts.installer.sha512, "artifacts.installer")?;
        check_filename(&self.artifacts.zip.name, "artifacts.zip.name")?;
        check_filename(&self.artifacts.installer.name, "artifacts.installer.name")?;

        for file in &self.files {
            check_hash(&file.sha512, &file.path)?;
            check_filename(&file.path, "files[].path")?;
        }

        let mut seen: Vec<&str> = self.files.iter().map(|f| f.path.as_str()).collect();
        seen.sort_unstable();
        let count = seen.len();
        seen.dedup();
        if seen.len() != count {
            return Err(UpdateError::Invalid(
                "files contains duplicate paths, so the applied bytes would be ambiguous".into(),
            ));
        }

        Ok(())
    }
}

fn check_hash(hash: &str, field: &str) -> Result<()> {
    if hash.len() != SHA512_HEX_LEN || !hash.bytes().all(|b| b.is_ascii_hexdigit()) {
        return Err(UpdateError::Invalid(format!(
            "{field}: sha512 must be {SHA512_HEX_LEN} hex characters"
        )));
    }
    Ok(())
}

/// Rejects anything that is not a bare filename.
///
/// This is the zip-slip guard, enforced at parse time rather than at extraction
/// time so no caller can forget it. Path separators, drive letters, and `..` all
/// escape the target directory, and the updater writes as an elevated process.
fn check_filename(name: &str, field: &str) -> Result<()> {
    let bad = name.is_empty()
        || name.contains('/')
        || name.contains('\\')
        || name.contains(':')
        || name == "."
        || name == ".."
        || name.starts_with('.');

    if bad {
        return Err(UpdateError::Invalid(format!(
            "{field}: {name:?} is not a bare filename"
        )));
    }
    Ok(())
}
