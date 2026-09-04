//! Removing files earlier versions left behind.
//!
//! Nothing else removes them: the updater's manifest only lists what a release
//! *writes*, and only the NSIS installer deletes anything, so a machine that
//! started on 1.4.x keeps its leftovers forever.
//!
//! Deleting files is a convenience that turns into a bug report, so the sweep is
//! narrow: compiled-in exact names, two derived directories, bare filenames
//! inside them, and regular singly-linked files. Each of those limits is a test
//! at the foot of this file, and the test names are the specification.

use std::path::Path;

/// Logs from the pre-1.5.0 C# plugin, which used NLog with its own rotation.
///
/// The 1.5.0 core writes `mbrc-core.log` and rotates it itself, so nothing has
/// written these since the rewrite. They are the bulk of what is left behind -
/// six files, and megabytes rather than bytes.
const LEGACY_LOGS: &[&str] = &[
    "mbrc.log",
    "mbrc.0.log",
    "mbrc.1.log",
    "mbrc.2.log",
    "mbrc.3.log",
    "mbrc.4.log",
    // 1.4.x named its log this before the NLog rotation existed.
    "error.log",
];

/// The C# firewall utility, replaced by `mbrc-helper.exe` in 1.5.0.
///
/// The NSIS installer deletes it on install and uninstall; this covers the
/// routes it does not run on. A 1.4.x `License.txt` also turns up beside our
/// own `LICENSE`, and is deliberately absent from this list: the plugins folder
/// is shared with every other plugin, so a generically named file there cannot
/// be shown to be ours, and "probably" is not the standard for a delete.
const RETIRED_PLUGIN_FILES: &[&str] = &["firewall-utility.exe"];

/// The cover state file, renamed rather than deleted when that state moved into
/// redb, as a safety net for a migration that has long since shipped.
const MIGRATED_COVER_STATE: &str = "state.json.migrated";

/// Sweeps the storage directory, and the plugins directory when one was
/// resolved.
///
/// A relative storage path is refused: it would resolve against MusicBee's
/// install directory. Writability is not checked first - on a standard install
/// the plugins directory is not writable and the failed delete is correct, since
/// the installer removes that file itself. Portable and Store installs have no
/// installer, which is the gap this fills. Every outcome is advisory.
pub fn sweep(storage_path: &str, plugins_dir: Option<&Path>) {
    let storage = Path::new(storage_path);
    if !storage.is_absolute() {
        tracing::warn!(
            path = %storage.display(),
            "not sweeping: the storage path is not absolute"
        );
        return;
    }

    let mut removed = 0usize;
    let mut freed = 0u64;
    for name in LEGACY_LOGS {
        if let Some(size) = remove_file(storage, name) {
            removed += 1;
            freed += size;
        }
    }

    if migrated_settings_are_present(storage) {
        if let Some(size) = remove_file(storage, "settings.xml") {
            removed += 1;
            freed += size;
        }
    }

    let cache = storage.join("cache");
    if is_real_directory(&cache) {
        if let Some(size) = remove_file(&cache, MIGRATED_COVER_STATE) {
            removed += 1;
            freed += size;
        }
    }

    if let Some(plugins) = plugins_dir {
        for name in RETIRED_PLUGIN_FILES {
            if let Some(size) = remove_file(plugins, name) {
                removed += 1;
                freed += size;
            }
        }
    }

    if removed > 0 {
        tracing::info!(
            files = removed,
            freed_bytes = freed,
            "removed files left behind by an earlier version"
        );
    }
}

/// Removes one bare filename from `dir`, returning the bytes reclaimed.
///
/// `None` covers every uninteresting case - not there, not a regular file, or
/// not removable - because none of them changes what the caller does.
fn remove_file(dir: &Path, name: &str) -> Option<u64> {
    debug_assert!(
        !name.contains(['/', '\\', ':']) && name != ".." && name != ".",
        "legacy sweep names must be bare filenames"
    );
    let path = dir.join(name);

    // `symlink_metadata` does not follow: a reparse point named like one of our
    // files is left exactly where it is.
    let meta = std::fs::symlink_metadata(&path).ok()?;
    if !meta.is_file() {
        if meta.file_type().is_symlink() {
            tracing::warn!(path = %path.display(), "leaving a link that shares a retired file's name");
        }
        return None;
    }

    if is_multiply_linked(&path, &meta) {
        tracing::warn!(
            path = %path.display(),
            "leaving a retired name that could not be shown to be safe to unlink"
        );
        return None;
    }

    let size = meta.len();
    match std::fs::remove_file(&path) {
        Ok(()) => {
            tracing::debug!(path = %path.display(), size, "removed a file from an earlier version");
            Some(size)
        }
        Err(e) => {
            tracing::debug!(path = %path.display(), error = %e, "could not remove a file from an earlier version");
            None
        }
    }
}

/// Whether `core_settings.json` is there *and* has something in it.
///
/// Gates removing `settings.xml`, which stays until the core has its own
/// settings: until then it is a 1.4.x user's only configuration. Afterwards it
/// must go, or deleting the JSON to reset the settings would silently restore
/// the old ones. Non-empty rather than present, because the migration writes
/// create-truncate-write and a second MusicBee starting at that instant would
/// see a zero-length file and delete the only copy.
fn migrated_settings_are_present(storage: &Path) -> bool {
    std::fs::metadata(storage.join("core_settings.json"))
        .map(|meta| meta.is_file() && meta.len() > 0)
        .unwrap_or(false)
}

/// Whether `path` is a directory in its own right, rather than a link to one.
///
/// Absent is not an error - there is simply nothing to sweep - and neither is a
/// junction: it just means the sweep stops rather than following it.
fn is_real_directory(path: &Path) -> bool {
    match std::fs::symlink_metadata(path) {
        Ok(meta) if meta.is_dir() => true,
        Ok(meta) => {
            if meta.file_type().is_symlink() {
                tracing::warn!(
                    path = %path.display(),
                    "not sweeping through a link that stands where a directory should"
                );
            }
            false
        }
        Err(_) => false,
    }
}

/// Whether more than one directory entry points at this file's data.
///
/// `is_file()` cannot see the difference, and the difference matters: unlinking
/// a name we recognise is fine, but only when that name is the only way to the
/// data. The link count is not on Windows' stable `Metadata`, so this takes a
/// handle, and anything unexpected - a file we cannot even open included -
/// counts as multiply linked and is left alone.
#[cfg(windows)]
fn is_multiply_linked(path: &Path, _meta: &std::fs::Metadata) -> bool {
    use std::os::windows::io::AsRawHandle;
    use windows_sys::Win32::Foundation::HANDLE;
    use windows_sys::Win32::Storage::FileSystem::{
        GetFileInformationByHandle, BY_HANDLE_FILE_INFORMATION,
    };

    let Ok(file) = std::fs::File::open(path) else {
        return true;
    };
    // SAFETY: an all-zero BY_HANDLE_FILE_INFORMATION is the documented starting
    // value, and the call below fills it.
    let mut info: BY_HANDLE_FILE_INFORMATION = unsafe { std::mem::zeroed() };
    // SAFETY: the handle is live for the call, and `info` is a plain
    // out-parameter of the size the API expects.
    let ok = unsafe { GetFileInformationByHandle(file.as_raw_handle() as HANDLE, &mut info) };
    if ok == 0 {
        return true;
    }
    info.nNumberOfLinks > 1
}

#[cfg(not(windows))]
fn is_multiply_linked(_path: &Path, meta: &std::fs::Metadata) -> bool {
    use std::os::unix::fs::MetadataExt;
    meta.nlink() > 1
}

#[cfg(test)]
mod tests {
    use super::*;

    fn scratch(case: &str) -> std::path::PathBuf {
        let dir = std::env::temp_dir().join(format!("mbrc-legacy-{case}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(dir.join("cache")).expect("scratch");
        dir
    }

    fn write(dir: &Path, name: &str, body: &str) {
        std::fs::write(dir.join(name), body).expect("write");
    }

    #[test]
    fn removes_the_old_logs_and_leaves_the_current_ones() {
        let dir = scratch("logs");
        for name in ["mbrc.log", "mbrc.0.log", "mbrc.4.log", "error.log"] {
            write(&dir, name, "old");
        }
        // Everything the 1.5 core owns must survive untouched.
        for name in [
            "mbrc-core.log",
            "mbrc-core.1.log.gz",
            "mbrc-bootstrap.log",
            "mbrc-helper.log",
            "mbrc.redb",
            "update_state.json",
        ] {
            write(&dir, name, "current");
        }

        sweep(dir.to_str().unwrap(), None);

        for name in ["mbrc.log", "mbrc.0.log", "mbrc.4.log", "error.log"] {
            assert!(!dir.join(name).exists(), "{name} should have been removed");
        }
        for name in [
            "mbrc-core.log",
            "mbrc-core.1.log.gz",
            "mbrc-bootstrap.log",
            "mbrc-helper.log",
            "mbrc.redb",
            "update_state.json",
        ] {
            assert!(dir.join(name).exists(), "{name} must be left alone");
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn keeps_legacy_settings_until_they_have_been_migrated() {
        let dir = scratch("unmigrated");
        write(&dir, "settings.xml", "<settings/>");

        // No core_settings.json yet: the migration still needs the XML, and
        // removing it here would lose a 1.4.x user's configuration outright.
        sweep(dir.to_str().unwrap(), None);
        assert!(dir.join("settings.xml").exists(), "not yet migrated");

        write(&dir, "core_settings.json", "{}");
        sweep(dir.to_str().unwrap(), None);
        assert!(
            !dir.join("settings.xml").exists(),
            "migrated, so the XML must go - otherwise deleting core_settings.json \
             to reset the settings silently restores the old ones instead"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn an_empty_settings_file_does_not_count_as_migrated() {
        // What a second MusicBee sees if it starts while the first is between
        // the create and the write of core_settings.json.
        let dir = scratch("half-written");
        write(&dir, "settings.xml", "<settings/>");
        write(&dir, "core_settings.json", "");

        sweep(dir.to_str().unwrap(), None);

        assert!(
            dir.join("settings.xml").exists(),
            "a zero-length settings file must not be taken as a completed migration"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn removes_the_retired_utility_from_the_plugins_directory() {
        let dir = scratch("plugins");
        let plugins = dir.join("Plugins");
        std::fs::create_dir_all(&plugins).expect("plugins");
        write(&plugins, "firewall-utility.exe", "retired");
        write(&plugins, "mb_remote.dll", "current");
        write(&plugins, "mbrc_core.dll", "current");
        write(&plugins, "mbrc-helper.exe", "current");

        sweep(dir.to_str().unwrap(), Some(&plugins));

        assert!(!plugins.join("firewall-utility.exe").exists());
        for name in ["mb_remote.dll", "mbrc_core.dll", "mbrc-helper.exe"] {
            assert!(plugins.join(name).exists(), "{name} must be left alone");
        }
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_directory_sharing_a_retired_name_is_not_touched() {
        // Nothing should ever create one, which is exactly why a delete that
        // would recurse into it must not be reachable.
        let dir = scratch("directory");
        std::fs::create_dir_all(dir.join("mbrc.log")).expect("directory");
        std::fs::write(dir.join("mbrc.log").join("inside"), "keep").expect("write");

        sweep(dir.to_str().unwrap(), None);

        assert!(dir.join("mbrc.log").join("inside").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_hard_linked_file_is_left_alone() {
        // The name would be ours to remove; the data behind it would not be.
        let dir = scratch("hardlink");
        write(&dir, "somebody-elses-file", "important");
        if std::fs::hard_link(dir.join("somebody-elses-file"), dir.join("mbrc.log")).is_err() {
            // Filesystems that cannot do this have nothing to test.
            let _ = std::fs::remove_dir_all(&dir);
            return;
        }

        sweep(dir.to_str().unwrap(), None);

        assert!(
            dir.join("mbrc.log").exists(),
            "a hard link must not be unlinked"
        );
        assert!(dir.join("somebody-elses-file").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_relative_storage_path_sweeps_nothing() {
        // Would otherwise resolve against MusicBee's install directory.
        let dir = scratch("relative");
        write(&dir, "mbrc.log", "must survive");
        let previous = std::env::current_dir().expect("cwd");
        std::env::set_current_dir(&dir).expect("chdir");

        sweep(".", None);

        std::env::set_current_dir(previous).expect("restore cwd");
        assert!(
            dir.join("mbrc.log").exists(),
            "a relative path must be refused, not resolved"
        );
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn the_sweep_does_not_follow_a_link_standing_in_for_the_cache_directory() {
        let dir = scratch("cache-link");
        // Somewhere else entirely, holding a file with the name we delete.
        let elsewhere = scratch("cache-link-target");
        write(&elsewhere, "state.json.migrated", "not ours to delete");

        std::fs::remove_dir_all(dir.join("cache")).expect("clear the real cache dir");
        #[cfg(windows)]
        let linked = std::os::windows::fs::symlink_dir(&elsewhere, dir.join("cache")).is_ok();
        #[cfg(not(windows))]
        let linked = std::os::unix::fs::symlink(&elsewhere, dir.join("cache")).is_ok();
        if !linked {
            // Creating one needs a privilege we may not have; nothing to prove.
            let _ = std::fs::remove_dir_all(&dir);
            let _ = std::fs::remove_dir_all(&elsewhere);
            return;
        }

        sweep(dir.to_str().unwrap(), None);

        assert!(
            elsewhere.join("state.json.migrated").exists(),
            "a junction in place of cache/ must stop the sweep, not redirect it"
        );
        let _ = std::fs::remove_dir_all(&dir);
        let _ = std::fs::remove_dir_all(&elsewhere);
    }

    #[test]
    fn sweeping_an_already_clean_directory_does_nothing() {
        let dir = scratch("clean");
        write(&dir, "mbrc-core.log", "current");
        sweep(dir.to_str().unwrap(), None);
        assert!(dir.join("mbrc-core.log").exists());
        let _ = std::fs::remove_dir_all(&dir);
    }

    #[test]
    fn a_missing_storage_directory_is_not_an_error() {
        let dir = std::env::temp_dir().join("mbrc-legacy-absent");
        let _ = std::fs::remove_dir_all(&dir);
        sweep(dir.to_str().unwrap(), None);
    }
}
