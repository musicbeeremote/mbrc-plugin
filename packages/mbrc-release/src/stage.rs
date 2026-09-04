//! Downloading a verified update and laying it out for the helper to apply.
//!
//! # What this is allowed to touch
//!
//! Staging runs unelevated, but an administrator process later reads everything
//! it writes, so the boundaries are part of the design:
//!
//! - **One directory, derived not supplied.** Everything lands under
//!   `<storage>/updates/<version>/`, and no value from the archive or manifest
//!   contributes a path segment that is not a bare filename
//!   ([`crate::manifest::is_bare_filename`]).
//! - **Nothing is followed.** The staging root and per-version directory are
//!   refused if they are symlinks or junctions, so a reparse point in
//!   `%APPDATA%` cannot aim the later elevated copy elsewhere.
//! - **`pending.json` holds no paths.** It names a version and the helper
//!   derives the storage directory itself (#151).
//! - **Staging-time verification is not apply-time verification.** The staged
//!   files sit somewhere unelevated code can write, so `manifest.json` and its
//!   `.minisig` are staged alongside the payload for the helper to re-verify.

use std::io::Read;
use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};
use time::{format_description::well_known::Rfc3339, OffsetDateTime};

use crate::{
    check::{AvailableUpdate, MANIFEST_ASSET, SIGNATURE_ASSET},
    error::{Result, UpdateError},
    http::HttpClient,
    manifest::{is_bare_filename, Manifest},
    verify::verify_sha512,
};

/// Staging root under the core's storage directory. Inside
/// `%APPDATA%\MusicBee\mb_remote`, which the NSIS uninstaller already removes
/// wholesale, so a staged update never outlives the plugin.
pub const STAGING_DIR: &str = "updates";

/// The commit marker. Written last, so its presence means every file in the
/// directory has been extracted and verified.
pub const PENDING_FILE: &str = "pending.json";

/// Schema of [`Pending`]; the helper refuses anything else (#151).
pub const PENDING_SCHEMA: u32 = 1;

/// What the helper reads to find out there is something to apply.
///
/// Note what is absent: any path. The helper derives the storage directory
/// itself and joins [`STAGING_DIR`] and `version` to it, so a tampered
/// `pending.json` can name a directory that does not exist but cannot name one
/// outside the staging root.
#[derive(Debug, Clone, PartialEq, Eq, Serialize, Deserialize)]
pub struct Pending {
    pub schema: u32,
    /// The staged version, and the name of its directory under [`STAGING_DIR`].
    /// Always a bare filename; the helper re-checks before joining it.
    pub version: String,
    pub staged_at: String,
    /// The staged payload filenames, for logging and for the panel. Not the
    /// authority on what gets applied: that is the staged manifest, which is
    /// signed.
    pub files: Vec<String>,
}

/// Where a staged update ended up.
#[derive(Debug, Clone, PartialEq, Eq)]
pub struct StagedUpdate {
    pub dir: PathBuf,
    pub version: String,
    pub files: Vec<String>,
}

/// Downloads the zip named by the verified manifest, extracts exactly the files
/// the manifest lists, and writes [`PENDING_FILE`] once every one of them has
/// verified.
///
/// The archive is held in memory and never written to disk as a whole: a
/// half-extracted, unverified zip lying next to the staged files is one more
/// thing that has to be reasoned about.
///
/// # Errors
/// An archive entry is not a bare filename, a file does not match the
/// manifest, or the staging directory could not be written.
pub fn stage(
    client: &dyn HttpClient,
    update: &AvailableUpdate,
    storage_dir: &str,
    now: OffsetDateTime,
) -> Result<StagedUpdate> {
    let manifest = &update.manifest;
    let version = &manifest.version;
    if !is_bare_filename(version) {
        return Err(UpdateError::Invalid(format!(
            "version {version:?} cannot be a directory name"
        )));
    }

    let root = Path::new(storage_dir).join(STAGING_DIR);
    let dir = root.join(version);
    prepare_dir(&root)?;
    // A previous attempt may have left a partial directory. It is ours and it is
    // named after this exact version, so it is replaced rather than merged with.
    if dir.exists() {
        refuse_reparse_point(&dir)?;
        std::fs::remove_dir_all(&dir).map_err(|e| io(&dir, e))?;
    }

    let payload = download_zip(client, update)?;
    let files = extract_verified(&payload, manifest)?;

    std::fs::create_dir_all(&dir).map_err(|e| io(&dir, e))?;
    for (name, bytes) in &files {
        write_file(&dir.join(name), bytes)?;
    }
    // The signed manifest and its signature travel with the payload so the
    // elevated apply re-verifies from these, not from anything we tell it.
    write_file(&dir.join(MANIFEST_ASSET), &update.manifest_bytes)?;
    write_file(&dir.join(SIGNATURE_ASSET), update.signature.as_bytes())?;

    let names: Vec<String> = files.into_iter().map(|(name, _)| name).collect();
    let pending = Pending {
        schema: PENDING_SCHEMA,
        version: version.clone(),
        staged_at: now.format(&Rfc3339).unwrap_or_default(),
        files: names.clone(),
    };
    let json = serde_json::to_string_pretty(&pending)
        .map_err(|e| UpdateError::Io(format!("serialize {PENDING_FILE}: {e}")))?;
    write_file(&root.join(PENDING_FILE), json.as_bytes())?;

    Ok(StagedUpdate {
        dir,
        version: version.clone(),
        files: names,
    })
}

/// Reads [`PENDING_FILE`], if there is one. A corrupt or unknown-schema marker is
/// an error rather than "nothing staged": something wrote it, and silently
/// ignoring it would hide that.
///
/// # Errors
/// The marker exists but does not parse, or names a version that is not a
/// bare directory name.
pub fn read_pending(storage_dir: &str) -> Result<Option<Pending>> {
    let path = Path::new(storage_dir).join(STAGING_DIR).join(PENDING_FILE);
    let Ok(contents) = std::fs::read_to_string(&path) else {
        return Ok(None);
    };
    let pending: Pending = serde_json::from_str(&contents)
        .map_err(|e| UpdateError::Parse(format!("{PENDING_FILE}: {e}")))?;
    if pending.schema != PENDING_SCHEMA {
        return Err(UpdateError::Invalid(format!(
            "{PENDING_FILE}: unsupported schema {}",
            pending.schema
        )));
    }
    if !is_bare_filename(&pending.version) {
        return Err(UpdateError::Invalid(format!(
            "{PENDING_FILE}: version {:?} cannot be a directory name",
            pending.version
        )));
    }
    Ok(Some(pending))
}

/// Removes a staged update and its marker. Used after a successful apply and
/// when the user cancels one.
pub fn clear_staged(storage_dir: &str) -> Result<()> {
    let root = Path::new(storage_dir).join(STAGING_DIR);
    if !root.exists() {
        return Ok(());
    }
    refuse_reparse_point(&root)?;
    std::fs::remove_dir_all(&root).map_err(|e| io(&root, e))
}

fn download_zip(client: &dyn HttpClient, update: &AvailableUpdate) -> Result<Vec<u8>> {
    let expected = &update.manifest.artifacts.zip;
    let body = client
        .get(&update.zip_url, None)?
        .into_body(&update.zip_url)?;
    // The size check is not security - the hash below is - but it turns a
    // truncated download into an error that says so.
    if body.len() as u64 != expected.size {
        return Err(UpdateError::Invalid(format!(
            "{}: manifest says {} bytes, download is {}",
            expected.name,
            expected.size,
            body.len()
        )));
    }
    verify_sha512(&body, &expected.sha512, &expected.name)?;
    Ok(body)
}

/// Pulls every file the manifest lists out of the archive and verifies it,
/// returning them all only if all of them verified. Nothing is written until
/// this has succeeded, so a bundle with one bad file leaves no files behind.
fn extract_verified(zip_bytes: &[u8], manifest: &Manifest) -> Result<Vec<(String, Vec<u8>)>> {
    let mut archive = zip::ZipArchive::new(std::io::Cursor::new(zip_bytes))
        .map_err(|e| UpdateError::Archive(e.to_string()))?;

    // Refuse the whole archive over one unsafe name: CI produces a flat zip,
    // so anything else means the bytes are not what the manifest describes.
    for index in 0..archive.len() {
        let entry = archive
            .by_index_raw(index)
            .map_err(|e| UpdateError::Archive(e.to_string()))?;
        let name = entry.name().to_owned();
        if !is_bare_filename(&name) {
            return Err(UpdateError::UnsafeEntry(name));
        }
    }

    // Driven by the manifest, not by the archive: an entry the manifest does not
    // list is never read and never written, whatever it claims to be.
    let mut extracted = Vec::with_capacity(manifest.files.len());
    for file in &manifest.files {
        let mut entry = archive
            .by_name(&file.path)
            .map_err(|_| UpdateError::MissingEntry(file.path.clone()))?;
        let mut bytes = Vec::with_capacity(entry.size() as usize);
        entry
            .read_to_end(&mut bytes)
            .map_err(|e| UpdateError::Archive(format!("{}: {e}", file.path)))?;
        verify_sha512(&bytes, &file.sha512, &file.path)?;
        extracted.push((file.path.clone(), bytes));
    }
    Ok(extracted)
}

/// Creates the staging root, refusing to use it if it is a link to somewhere
/// else.
fn prepare_dir(root: &Path) -> Result<()> {
    if root.exists() {
        refuse_reparse_point(root)?;
        if !root.is_dir() {
            return Err(UpdateError::Io(format!(
                "{} exists and is not a directory",
                root.display()
            )));
        }
    }
    std::fs::create_dir_all(root).map_err(|e| io(root, e))
}

/// Refuses a path that is a symlink or, on Windows, a junction.
///
/// `symlink_metadata` does not follow the link, so this sees the reparse point
/// itself. Without it, `remove_dir_all` and every write below would land
/// wherever the link points - which is the whole trick, given the helper later
/// copies these files while elevated.
fn refuse_reparse_point(path: &Path) -> Result<()> {
    let metadata = std::fs::symlink_metadata(path).map_err(|e| io(path, e))?;
    if metadata.file_type().is_symlink() {
        return Err(UpdateError::Io(format!(
            "{} is a link; refusing to stage through it",
            path.display()
        )));
    }
    Ok(())
}

/// Writes one staged file, refusing to write through an existing link.
fn write_file(path: &Path, bytes: &[u8]) -> Result<()> {
    if path.exists() {
        refuse_reparse_point(path)?;
    }
    std::fs::write(path, bytes).map_err(|e| io(path, e))
}

fn io(path: &Path, e: std::io::Error) -> UpdateError {
    UpdateError::Io(format!("{}: {e}", path.display()))
}
