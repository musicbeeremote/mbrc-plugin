//! Refuses client-named paths that are Windows system paths or on unknown servers.
//!
//! Handing MusicBee `\\attacker\share\a.mp3` could make Windows authenticate to that
//! host with the user's NTLM credentials, so a client that may name a path must not
//! be able to name an arbitrary server. UNC paths cannot simply be refused, since a
//! library on a NAS is made of them. A server is known when the library, the queue,
//! a media source or a playlist already holds a path on it. The host is asked only
//! when a request names a network path, so an ordinary request costs nothing.
//!
//! Device paths, pipes, admin shares, reserved names, data streams and the Windows
//! folder hold no music and are refused wherever they are, even on a known server.

use std::collections::HashSet;

use serde_json::Value;

use crate::ffi::types::LibrarySource;
use crate::providers::Providers;

/// The server a path would reach over the network, lowercased; `None` if it is local.
///
/// A device-namespace path on anything but a drive letter counts as a server of its
/// own name, so it is refused unless the library somehow holds one.
pub(crate) fn remote_server(path: &str) -> Option<String> {
    let path = path.trim_start();
    let rest = match path.get(..5) {
        Some(scheme) if scheme.eq_ignore_ascii_case("file:") => {
            let rest = &path[5..];
            let slashes = rest.len() - rest.trim_start_matches(is_slash).len();
            if slashes < 2 || slashes == 3 {
                return None;
            }
            rest.trim_start_matches(is_slash)
        }
        _ if path.starts_with(r"\??\") => &path[1..],
        _ if path.chars().take(2).filter(|c| is_slash(*c)).count() == 2 => {
            path.trim_start_matches(is_slash)
        }
        _ => return None,
    };

    let mut segments = rest.split(is_slash);
    let first = segments.next().unwrap_or_default();
    if first != "?" && first != "??" && first != "." {
        return Some(first.to_ascii_lowercase());
    }
    let device = segments.next().unwrap_or_default();
    if device.eq_ignore_ascii_case("unc") {
        return Some(segments.next().unwrap_or_default().to_ascii_lowercase());
    }
    let is_drive = device.len() == 2 && device.ends_with(':');
    (!is_drive).then(|| device.to_ascii_lowercase())
}

fn is_slash(c: char) -> bool {
    c == '\\' || c == '/'
}

/// Whether `path` is a URL for something other than a file, such as a radio stream.
fn is_non_file_url(path: &str) -> bool {
    path.split_once("://").is_some_and(|(scheme, _)| {
        scheme.len() > 1
            && scheme.chars().all(|c| c.is_ascii_alphanumeric())
            && !scheme.eq_ignore_ascii_case("file")
    })
}

/// The path with `.` and `..` resolved and each part as Windows would match it.
///
/// Windows ignores case and trailing dots and spaces, so `WINDOWS. \a` is the Windows
/// folder. `..` stops at the root, as it does on disk.
fn resolved(segments: &[&str]) -> Vec<String> {
    let mut parts: Vec<String> = Vec::new();
    for segment in segments {
        match *segment {
            "" | "." => {}
            ".." => {
                parts.pop();
            }
            part => {
                let part = part.trim_end_matches(['.', ' ']);
                if !part.is_empty() {
                    parts.push(part.to_ascii_lowercase());
                }
            }
        }
    }
    parts
}

/// A share that exposes the machine rather than files someone chose to share.
fn is_system_share(share: &str) -> bool {
    matches!(share, "admin$" | "ipc$" | "print$" | "pipe" | "mailslot")
        || (share.len() == 2 && share.ends_with('$'))
}

/// A device name, which Windows reserves in every folder and with any extension.
fn is_reserved_name(part: &str) -> bool {
    let stem = part.split('.').next().unwrap_or(part).trim_end();
    let numbered = |prefix: &str| {
        stem.strip_prefix(prefix)
            .is_some_and(|n| n.len() == 1 && n.as_bytes()[0].is_ascii_digit())
    };
    matches!(stem, "con" | "prn" | "aux" | "nul" | "conin$" | "conout$")
        || numbered("com")
        || numbered("lpt")
}

/// Whether a path names a Windows system location rather than a file of the user's.
pub(crate) fn is_system_path(path: &str) -> bool {
    let path = path.trim_start();
    if is_non_file_url(path) {
        return false;
    }
    let (rest, mut network) = match path.get(..5) {
        Some(scheme) if scheme.eq_ignore_ascii_case("file:") => {
            let rest = &path[5..];
            let slashes = rest.len() - rest.trim_start_matches(is_slash).len();
            (rest, slashes >= 2 && slashes != 3)
        }
        _ => (
            path,
            path.starts_with(r"\??\") || path.chars().take(2).all(is_slash) && path.len() >= 2,
        ),
    };
    let segments: Vec<&str> = rest.split(is_slash).filter(|s| !s.is_empty()).collect();
    let mut tail = segments.as_slice();

    if network && matches!(tail.first(), Some(&"?" | &"??" | &".")) {
        let device = tail.get(1).copied().unwrap_or_default();
        if device.eq_ignore_ascii_case("unc") {
            tail = &tail[2..];
        } else if device.len() == 2 && device.ends_with(':') {
            tail = &tail[1..];
            network = false;
        } else {
            return true;
        }
    }

    let parts = if network {
        resolved(tail.get(1..).unwrap_or_default())
    } else {
        resolved(tail)
    };
    let mut after_root = parts.as_slice();
    if network {
        if parts.first().is_some_and(|share| is_system_share(share)) {
            return true;
        }
    } else if parts
        .first()
        .is_some_and(|part| part.len() == 2 && part.ends_with(':'))
    {
        after_root = &parts[1..];
    }
    if after_root
        .first()
        .is_some_and(|part| part == "windows" || part.starts_with("window~"))
    {
        return true;
    }
    after_root.iter().any(|part| {
        part.contains(':')
            || is_reserved_name(part)
            || part == "$recycle.bin"
            || part == "system volume information"
    })
}

/// Every network server the host's own lists already use.
///
/// A list the host fails to give counts as empty, so a failure refuses rather than
/// admits.
fn known_servers(p: &dyn Providers) -> HashSet<String> {
    let mut paths = p.track_paths().unwrap_or_default();
    paths.extend(p.now_playing_list_paths().unwrap_or_default());
    for source in [
        LibrarySource::Inbox,
        LibrarySource::Audiobooks,
        LibrarySource::Videos,
    ] {
        paths.extend(p.media_paths(source).unwrap_or_default());
    }
    if let Ok(playlists) = p.playlists(0, 0) {
        paths.extend(playlists.data.into_iter().map(|playlist| playlist.url));
    }
    paths
        .iter()
        .filter_map(|path| remote_server(path))
        .collect()
}

/// The first of `paths` that is a system path, or else on a server the host does not use.
pub(crate) fn first_refused<'a>(
    paths: impl IntoIterator<Item = &'a str>,
    p: &dyn Providers,
) -> Option<&'a str> {
    let paths: Vec<&str> = paths.into_iter().collect();
    if let Some(system) = paths.iter().find(|path| is_system_path(path)) {
        return Some(system);
    }
    let remote: Vec<(&str, String)> = paths
        .into_iter()
        .filter_map(|path| Some((path, remote_server(path)?)))
        .collect();
    if remote.is_empty() {
        return None;
    }
    let known = known_servers(p);
    remote
        .into_iter()
        .find(|(_, server)| !known.contains(server))
        .map(|(path, _)| path)
}

/// Every string a V6 request names a track or playlist file with, and its field.
///
/// Read by key rather than per op, so an op added later with one of these names is
/// covered without having to remember this check.
pub(crate) fn named_paths(data: &Value) -> Vec<(&'static str, &str)> {
    let mut named: Vec<(&'static str, &str)> = ["src", "path", "play", "url"]
        .into_iter()
        .filter_map(|field| Some((field, data.get(field)?.as_str()?)))
        .collect();
    if let Some(paths) = data.get("paths").and_then(Value::as_array) {
        named.extend(paths.iter().filter_map(Value::as_str).map(|p| ("paths", p)));
    }
    named
}

#[cfg(test)]
mod tests {
    use serde_json::json;

    use super::*;
    use crate::providers::MockProviders;

    #[test]
    fn ordinary_music_paths_are_not_system_paths() {
        for path in [
            r"C:\Music\a.mp3",
            "C:/Users/me/Music/a.flac",
            r"D:\Windows Music\a.mp3",
            r"D:\Windowsill\a.mp3",
            r"E:\Console Games\Conan.mp3",
            r"\\nas\music\a.mp3",
            r"\\nas\music$\a.mp3",
            r"\\?\UNC\nas\music\a.mp3",
            r"\\?\C:\Music\a.mp3",
            "file:///C:/Music/a.mp3",
            "a.mp3",
            "",
            "http://radio.example:8000/stream",
            "playlist://x",
            r"C:\Music\Nullsleep - COMA.mp3",
        ] {
            assert!(!is_system_path(path), "{path}");
        }
    }

    #[test]
    fn device_paths_are_system_paths() {
        for path in [
            r"\\.\pipe\x",
            r"\\.\PhysicalDrive0",
            r"\\?\GLOBALROOT\Device\HarddiskVolume1\a",
            r"\\?\Volume{1234}\a.mp3",
            r"\??\pipe\x",
            "//./pipe/x",
        ] {
            assert!(is_system_path(path), "{path}");
        }
    }

    #[test]
    fn machine_shares_are_system_paths_on_any_server() {
        for path in [
            r"\\nas\c$\a.mp3",
            r"\\localhost\C$\Users\a.mp3",
            r"\\127.0.0.1\ADMIN$\a",
            r"\\nas\IPC$",
            r"\\nas\pipe\x",
            r"\\nas\music\..\c$\a.mp3",
            r"\\?\UNC\nas\admin$\a",
            "file://nas/c$/a.mp3",
        ] {
            assert!(is_system_path(path), "{path}");
        }
    }

    #[test]
    fn reserved_names_and_data_streams_are_system_paths() {
        for path in [
            "CON",
            r"C:\Music\nul",
            r"C:\Music\NUL.mp3",
            r"C:\Music\com1.flac",
            r"\\nas\music\LPT9.txt",
            r"C:\Music\a.mp3:hidden",
            r"C:\Music\a.mp3::$DATA",
            r"C:\$Recycle.Bin\a.mp3",
            r"C:\System Volume Information\a",
        ] {
            assert!(is_system_path(path), "{path}");
        }
    }

    #[test]
    fn every_way_of_naming_the_windows_folder_is_a_system_path() {
        for path in [
            r"C:\Windows\win.ini",
            "c:/windows/win.ini",
            r"C:\Music\..\Windows\win.ini",
            r"C:\..\..\Windows\win.ini",
            r"C:\.\WINDOWS.\win.ini",
            r"C:\WINDOW~1\win.ini",
            r"\\?\C:\Windows\win.ini",
            "file:///C:/Windows/win.ini",
            r"\Windows\win.ini",
        ] {
            assert!(is_system_path(path), "{path}");
        }
    }

    /// A known server opens no exception for its machine shares.
    #[test]
    fn a_known_server_does_not_excuse_a_system_share() {
        let host = MockProviders {
            track_paths: vec![r"\\nas\music\a.mp3".into()],
            ..Default::default()
        };
        let share = r"\\nas\c$\a.mp3";
        assert_eq!(first_refused([share], &host), Some(share));
    }

    #[test]
    fn local_paths_and_streams_reach_no_server() {
        for path in [
            r"C:\Music\a.mp3",
            "C:/Music/a.mp3",
            r"\Music\a.mp3",
            "a.mp3",
            "",
            "https://radio.example/stream",
            "file:///C:/Music/a.mp3",
            r"\\?\C:\Music\a.mp3",
            r"\\.\C:\Music\a.mp3",
            r"\??\C:\Music\a.mp3",
        ] {
            assert_eq!(remote_server(path), None, "{path}");
        }
    }

    #[test]
    fn every_spelling_of_a_network_path_names_its_server() {
        for path in [
            r"\\NAS\music\a.mp3",
            "//nas/music/a.mp3",
            r"\/nas\music",
            r"  \\nas\music",
            r"\\?\UNC\nas\music\a.mp3",
            r"\\.\unc\nas\music\a.mp3",
            r"\??\UNC\nas\music\a.mp3",
            "file://nas/music/a.mp3",
            "FILE:////nas/music/a.mp3",
            r"file:\\nas\music\a.mp3",
        ] {
            assert_eq!(remote_server(path).as_deref(), Some("nas"), "{path}");
        }
    }

    #[test]
    fn a_device_path_counts_as_a_server_of_its_own() {
        assert_eq!(remote_server(r"\\.\pipe\x").as_deref(), Some("pipe"));
        assert!(remote_server(r"\\?\Volume{1234}\a.mp3").is_some());
    }

    #[test]
    fn a_server_the_library_uses_is_known() {
        let host = MockProviders {
            track_paths: vec![r"\\NAS\music\a.mp3".into()],
            ..Default::default()
        };
        assert_eq!(first_refused([r"\\nas\other\b.mp3"], &host), None);
    }

    #[test]
    fn a_server_the_queue_uses_is_known() {
        let host = MockProviders {
            now_playing_list_paths: vec![r"\\nas\music\a.mp3".into()],
            ..Default::default()
        };
        assert_eq!(first_refused([r"\\nas\music\b.mp3"], &host), None);
    }

    #[test]
    fn a_server_nothing_uses_is_refused_beside_good_paths() {
        let host = MockProviders {
            track_paths: vec![r"\\nas\music\a.mp3".into()],
            ..Default::default()
        };
        let evil = r"\\attacker\share\a.mp3";
        assert_eq!(
            first_refused([r"C:\a.mp3", r"\\nas\music\b.mp3", evil], &host),
            Some(evil)
        );
    }

    #[test]
    fn a_request_naming_only_local_paths_never_asks_the_host() {
        let host = MockProviders::default();
        assert_eq!(first_refused([r"C:\a.mp3", "b.mp3"], &host), None);
        assert!(host.recorded().is_empty(), "{:?}", host.recorded());
    }

    #[test]
    fn named_paths_reads_each_field_and_ignores_other_shapes() {
        let data = json!({
            "src": "a", "path": "b", "play": "c", "url": "d",
            "paths": ["e", 5, "f"], "query": r"\\not\a\path",
        });
        let named = named_paths(&data);
        let fields: Vec<_> = named.iter().map(|(_, path)| *path).collect();
        assert_eq!(fields, ["a", "b", "c", "d", "e", "f"]);
        assert!(named_paths(&json!({"play": true, "paths": "x"})).is_empty());
    }

    fn dispatch(
        op: &str,
        data: &Value,
        host: &MockProviders,
    ) -> crate::server::commands_v6::OpResult {
        crate::server::commands_v6::dispatch(op, data, host, None, None, None).expect("a known op")
    }

    /// Each op the guard exists for, refused before the host is handed the path.
    #[test]
    fn v6_ops_refuse_an_unknown_server_and_name_the_field() {
        let evil = r"\\attacker\share\a.mp3";
        for (op, data, field) in [
            (
                "now_playing_queue",
                json!({"paths": [evil], "mode": "last"}),
                "paths",
            ),
            (
                "now_playing_queue",
                json!({"paths": ["a.mp3"], "play": evil}),
                "play",
            ),
            ("track_get", json!({"src": evil}), "src"),
            ("playlist_tracks", json!({"url": evil}), "url"),
            ("playlist_delete", json!({"url": evil}), "url"),
        ] {
            let host = MockProviders::default();
            let refused = dispatch(op, &data, &host).expect_err(op);
            assert_eq!(refused.code, mbrc_wire::v6::ErrorCode::InvalidField, "{op}");
            assert_eq!(refused.field.as_deref(), Some(field), "{op}");
        }
    }

    #[test]
    fn a_nas_library_keeps_queueing_its_own_files() {
        let host = MockProviders {
            track_paths: vec![r"\\nas\music\a.mp3".into()],
            ..Default::default()
        };
        let data = json!({"paths": [r"\\nas\music\b.mp3"], "mode": "last"});
        assert!(dispatch("now_playing_queue", &data, &host).is_ok());
    }

    proptest::proptest! {
        #[test]
        fn any_path_is_judged_without_panicking(path in "(?s).{0,60}") {
            let _ = remote_server(&path);
            let _ = is_system_path(&path);
        }
    }
}
