//! Removing files earlier versions left behind.
//!
//! Nothing removes them otherwise. The updater's manifest is an allowlist of
//! what a release *writes*; it has no notion of what a release *retires*, and
//! only the NSIS installer deletes anything - which covers exactly one of the
//! three ways the plugin gets installed. A zip extraction and the Store's "Add
//! Plugin" both only ever add files, so a machine that started on 1.4.x keeps
//! its 1.4.x leftovers forever.
//!
//! Deleting files is the kind of convenience that turns into a bug report, so
//! the rules here are deliberately narrow:
//!
//! - **Exact names, compiled in.** No globs and no "delete what looks old". The
//!   only pattern is a bounded numeric one for a rotation scheme we shipped
//!   ourselves, and it is spelled out rather than matched loosely.
//! - **Only two directories, and only bare names inside them.** The plugins
//!   directory is derived - asked of the loader, so it is true by construction.
//!   The storage directory is *not*: it arrives over FFI from the C# side, which
//!   takes it from MusicBee. That is not a path an attacker picks, but it is
//!   supplied rather than derived, so the names joined onto it carry the weight
//!   here - every one is a compile-time constant with no separator in it.
//! - **Regular files, singly linked.** A directory or a reparse point with a
//!   matching name is left alone: following one would let anything able to
//!   create it aim a delete somewhere else. So is a file with more than one hard
//!   link, which `is_file()` alone cannot tell apart from an ordinary one - the
//!   name would be ours to remove, but the data would not.
//! - **Absolute directories only.** A relative one would resolve against the
//!   process working directory, which for us is MusicBee's install folder - so
//!   an empty or relative storage path would aim these deletes at a directory
//!   nobody chose. The helper's `checked_path` refuses relative paths for the
//!   same reason.
//! - **No descending through a link.** The leaf check above does not cover the
//!   directory components above it, and `cache/` is the one component this
//!   walks through. A junction there would redirect the delete wholesale, so it
//!   has to be a real directory - the same rule the update staging code applies
//!   to its own directories.
//! - **Best effort, never fatal.** A file that cannot be removed is logged and
//!   skipped. This runs during startup and must never be the reason the plugin
//!   does not start.
//!
//! What is deliberately *not* defended against is the window between the check
//! and the delete. It does not need to be: both platforms unlink the name rather
//! than following it, so a link swapped in after the check is itself what gets
//! removed, never its target. And everything here runs unelevated, as the user
//! who already owns these directories - so there is no privilege to gain by
//! racing it.

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
/// The NSIS installer already deletes this on both install and uninstall; this
/// is for the routes it does not run on.
///
/// `License.txt` is deliberately absent, though a 1.4.x-era copy does turn up
/// beside our own `LICENSE` in the wild. It is probably ours, but the plugins
/// folder is shared with every other MusicBee plugin and a generically named
/// file there cannot be proven to be. Deleting someone else's file to reclaim
/// 38 KB is not a trade worth making, and "probably" is not the standard this
/// list is held to.
const RETIRED_PLUGIN_FILES: &[&str] = &["firewall-utility.exe"];

/// Sweeps the storage directory, and the plugins directory when one was
/// resolved.
///
/// Nothing checks whether either is writable first: on a standard installation
/// the plugins directory is not, and the delete simply fails and is skipped -
/// which is correct there anyway, because the installer removes that file on its
/// own route.
///
/// Returns nothing: every outcome here is advisory, and the caller has no
/// decision to make based on it.
pub fn sweep(storage_path: &str, plugins_dir: Option<&Path>) {
    let storage = Path::new(storage_path);
    if !storage.is_absolute() {
        // Relative would resolve against MusicBee's install directory. Nothing
        // should ever hand us one, which is exactly why it is worth saying so
        // instead of quietly deleting somewhere else.
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

    // `settings.xml` is only inert once the core has its own settings file.
    // Until then it is the one record of a 1.4.x user's configuration and
    // `migrate_legacy_settings` still needs it.
    //
    // Removing it afterwards is not tidiness for its own sake: the migration
    // runs whenever `core_settings.json` is absent, so leaving the XML in place
    // means deleting the JSON to reset the settings silently restores the old
    // ones instead. Reset should mean reset.
    if storage.join("core_settings.json").is_file() {
        if let Some(size) = remove_file(storage, "settings.xml") {
            removed += 1;
            freed += size;
        }
    }

    // Renamed rather than deleted when the cover state moved into redb, as a
    // safety net for a migration that has long since shipped.
    //
    // This is the only directory component the sweep walks through, and the
    // leaf's reparse-point check says nothing about it: a junction here would
    // redirect the delete somewhere else entirely.
    let cache = storage.join("cache");
    if is_real_directory(&cache) {
        if let Some(size) = remove_file(&cache, "state.json.migrated") {
            removed += 1;
            freed += size;
        }
    }

    // The plugins directory is not writable on a standard installation, and does
    // not need to be: the installer deletes this one itself. Portable and Store
    // installations are writable and have no installer, which is the gap.
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
        // Deliberately vague: this is also how "could not be opened to check"
        // arrives, and naming a cause we have not established would send whoever
        // reads it the wrong way.
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
            // Read-only media, a locked file, a directory we cannot write. All
            // of them mean "leave it", and none of them is worth a warning on
            // every start.
            tracing::debug!(path = %path.display(), error = %e, "could not remove a file from an earlier version");
            None
        }
    }
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
/// data. Anything unexpected here counts as multiply linked - refusing to delete
/// on a doubt costs a few kilobytes, and the alternative costs someone's file.
#[cfg(windows)]
fn is_multiply_linked(path: &Path, _meta: &std::fs::Metadata) -> bool {
    use std::os::windows::io::AsRawHandle;
    use windows_sys::Win32::Foundation::HANDLE;
    use windows_sys::Win32::Storage::FileSystem::{
        GetFileInformationByHandle, BY_HANDLE_FILE_INFORMATION,
    };

    // The link count is not on the stable `Metadata` for Windows, so it takes a
    // handle. Opening for read is enough, and a file we cannot even open is one
    // we are certainly not going to delete.
    let Ok(file) = std::fs::File::open(path) else {
        return true;
    };
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
