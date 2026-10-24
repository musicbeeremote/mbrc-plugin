//! This installation's `server_id`, sent in the V6 handshake (#250).
//!
//! A client keys its local library on the server it synced from. The address
//! cannot be that key: the same machine moves to a new IP, and two machines
//! take turns at one. So the core generates a UUID on first start and keeps it
//! in the storage directory, where it survives restarts, updates and address
//! changes, and changes only when that directory is wiped.
//!
//! Machine-written state, so it is not in `core_settings.json`: a save from the
//! settings panel must never be able to change it.

use std::io::ErrorKind;
use std::path::Path;
use std::sync::Arc;

/// The id's file name in the storage directory.
const FILE_NAME: &str = "server_id";

/// Who this server is, as the V6 handshake reports it.
#[derive(Debug, Clone)]
pub struct ServerIdentity {
    /// The installation's UUID; absent when it could not be read or kept.
    pub id: Option<Arc<str>>,
    /// The machine name, the same one discovery reports.
    pub name: Arc<str>,
}

impl ServerIdentity {
    /// Reads the id from `storage` (generating it on first start) and the
    /// machine name, once per core.
    pub fn load(storage: &str) -> Self {
        Self {
            id: load_or_create(storage).map(Arc::from),
            name: Arc::from(crate::discovery::hostname()),
        }
    }
}

/// Reads this installation's id from `storage`, generating and saving one on
/// first start.
///
/// `None` without a storage directory, when the file exists but cannot be read
/// (a locked file must not be mistaken for a missing one and overwritten), or
/// when a new id cannot be saved: an id that changes every start is worse than
/// none, since a client would resync its library on each one.
pub fn load_or_create(storage: &str) -> Option<String> {
    if storage.is_empty() {
        return None;
    }
    let path = Path::new(storage).join(FILE_NAME);
    match std::fs::read_to_string(&path) {
        Ok(saved) if is_uuid(saved.trim()) => return Some(saved.trim().to_owned()),
        Ok(_) => tracing::warn!("server_id file is not a UUID; generating a new id"),
        Err(e) if e.kind() == ErrorKind::NotFound => {}
        Err(e) => {
            tracing::warn!(error = %e, "server_id could not be read; the handshake omits it");
            return None;
        }
    }
    let id = new_uuid()?;
    match save(&path, &id) {
        Ok(()) => {
            tracing::info!(server_id = %id, "generated this installation's server_id");
            Some(id)
        }
        Err(e) => {
            tracing::warn!(error = %e, "server_id could not be saved; the handshake omits it");
            None
        }
    }
}

/// Writes `id` beside `path` and renames it into place, so a crash mid-write
/// leaves the old file or the new one, never a truncated id.
fn save(path: &Path, id: &str) -> std::io::Result<()> {
    let staged = path.with_extension("tmp");
    std::fs::write(&staged, id)?;
    std::fs::rename(&staged, path)
}

/// A random (version 4) UUID in lowercase hyphenated form.
fn new_uuid() -> Option<String> {
    let mut b = [0u8; 16];
    getrandom::fill(&mut b).ok()?;
    b[6] = (b[6] & 0x0f) | 0x40;
    b[8] = (b[8] & 0x3f) | 0x80;
    let hex = crate::server::clients::hex(&b);
    Some(format!(
        "{}-{}-{}-{}-{}",
        &hex[..8],
        &hex[8..12],
        &hex[12..16],
        &hex[16..20],
        &hex[20..]
    ))
}

/// Whether `s` is a hyphenated UUID, any version.
fn is_uuid(s: &str) -> bool {
    let groups: Vec<&str> = s.split('-').collect();
    groups.iter().map(|g| g.len()).eq([8, 4, 4, 4, 12])
        && groups
            .iter()
            .all(|g| g.bytes().all(|c| c.is_ascii_hexdigit()))
}

#[cfg(test)]
mod tests {
    use std::path::PathBuf;

    use super::*;

    fn dir(name: &str) -> PathBuf {
        let dir =
            std::env::temp_dir().join(format!("mbrc-server-id-{name}-{}", std::process::id()));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        dir
    }

    #[test]
    fn the_id_is_generated_once_and_kept_across_starts() {
        let dir = dir("kept");
        let first = load_or_create(&dir.to_string_lossy()).unwrap();
        let second = load_or_create(&dir.to_string_lossy()).unwrap();
        assert_eq!(first, second);
        assert!(is_uuid(&first), "{first}");
    }

    #[test]
    fn a_generated_id_is_a_version_4_uuid() {
        let id = new_uuid().unwrap();
        assert!(is_uuid(&id), "{id}");
        assert_eq!(&id[14..15], "4", "{id}");
        assert!(matches!(&id[19..20], "8" | "9" | "a" | "b"), "{id}");
    }

    #[test]
    fn a_damaged_file_is_replaced_with_a_fresh_id() {
        let dir = dir("damaged");
        std::fs::write(dir.join(FILE_NAME), "not a uuid").unwrap();
        let id = load_or_create(&dir.to_string_lossy()).unwrap();
        assert!(is_uuid(&id), "{id}");
        assert_eq!(std::fs::read_to_string(dir.join(FILE_NAME)).unwrap(), id);
    }

    #[test]
    fn an_id_saved_with_surrounding_whitespace_is_still_read() {
        let dir = dir("whitespace");
        let saved = "3f2b8c1e-9d4a-4b6f-8e2d-1a7c5b9e0f34";
        std::fs::write(dir.join(FILE_NAME), format!("{saved}\r\n")).unwrap();
        assert_eq!(
            load_or_create(&dir.to_string_lossy()).as_deref(),
            Some(saved)
        );
    }

    #[test]
    fn an_unreadable_file_is_left_alone_and_no_id_is_sent() {
        let dir = dir("unreadable");
        std::fs::create_dir_all(dir.join(FILE_NAME)).unwrap();
        assert_eq!(load_or_create(&dir.to_string_lossy()), None);
        assert!(
            dir.join(FILE_NAME).is_dir(),
            "the unreadable entry was replaced"
        );
    }

    #[test]
    fn saving_leaves_no_staged_file_behind() {
        let dir = dir("staged");
        load_or_create(&dir.to_string_lossy()).unwrap();
        assert!(!dir.join(FILE_NAME).with_extension("tmp").exists());
    }

    #[test]
    fn without_a_storage_directory_there_is_no_id() {
        assert_eq!(load_or_create(""), None);
    }
}
