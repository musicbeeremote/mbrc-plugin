//! MusicBee's cached artist backdrops (#280), read from disk with no API call.
//!
//! MusicBee keeps the backdrops it has shown in `InternalCache\ArtistBackdrops`,
//! one JPEG per artist named `<artist>.dat`, beside the `ArtistThumbs` folder
//! its thumbnail call answers from. It has backdrops for artists whose
//! thumbnail call answers none, so they are the fetcher's second local source.

use std::path::{Path, PathBuf};
use std::sync::Mutex;

/// MusicBee's backdrop folder, when there is one.
#[derive(Debug, Default)]
pub struct Backdrops {
    dir: Mutex<Option<PathBuf>>,
}

impl Backdrops {
    /// The folder MusicBee keeps beside `storage_path`, the plugin's own
    /// `mb_remote` folder inside MusicBee's AppData, until a thumbnail path
    /// names the real one ([`Self::learn_from_thumb`]).
    pub fn beside(storage_path: &Path) -> Self {
        let dir = storage_path
            .parent()
            .map(|app_data| app_data.join("InternalCache").join("ArtistBackdrops"));
        Self {
            dir: Mutex::new(dir),
        }
    }

    #[cfg(test)]
    pub fn in_dir(dir: PathBuf) -> Self {
        Self {
            dir: Mutex::new(Some(dir)),
        }
    }

    /// Takes the backdrop folder from a thumbnail MusicBee wrote: the
    /// `ArtistBackdrops` beside its `ArtistThumbs`. Any other path is ignored.
    pub fn learn_from_thumb(&self, thumb: &Path) {
        let Some(thumbs) = thumb.parent() else {
            return;
        };
        let is_thumbs = thumbs
            .file_name()
            .and_then(|n| n.to_str())
            .is_some_and(|n| n.eq_ignore_ascii_case("ArtistThumbs"));
        if let (true, Some(cache)) = (is_thumbs, thumbs.parent()) {
            *self.lock_dir() = Some(cache.join("ArtistBackdrops"));
        }
    }

    fn lock_dir(&self) -> std::sync::MutexGuard<'_, Option<PathBuf>> {
        self.dir.lock().unwrap_or_else(|e| e.into_inner())
    }

    /// The backdrop MusicBee cached for `artist`; `None` without one or when the
    /// file is empty.
    pub fn read(&self, artist: &str) -> Option<Vec<u8>> {
        let artist = artist.trim();
        if artist.is_empty() {
            return None;
        }
        let path = self.lock_dir().as_ref()?.join(file_name(artist));
        std::fs::read(path).ok().filter(|raw| !raw.is_empty())
    }
}

/// The file MusicBee names `artist`'s backdrop: every character Windows
/// forbids in a file name becomes a space, so `AC/DC` is `AC DC.dat`.
fn file_name(artist: &str) -> String {
    let stem: String = artist
        .chars()
        .map(|c| {
            if u32::from(c) < 32
                || matches!(c, '"' | '<' | '>' | '|' | ':' | '*' | '?' | '\\' | '/')
            {
                ' '
            } else {
                c
            }
        })
        .collect();
    format!("{stem}.dat")
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn a_forbidden_character_becomes_a_space_as_musicbee_names_it() {
        assert_eq!(file_name("AC/DC"), "AC DC.dat");
        assert_eq!(
            file_name("Alexis & Fido feat. Franco \"El Gorila\""),
            "Alexis & Fido feat. Franco  El Gorila .dat"
        );
        assert_eq!(file_name("Who? What: <*|>\\"), "Who  What       .dat");
        assert_eq!(file_name("Ñengo Flow"), "Ñengo Flow.dat");
    }

    #[test]
    fn a_name_cannot_reach_outside_the_backdrop_folder() {
        assert_eq!(file_name("../../secret"), ".. .. secret.dat");
        assert_eq!(file_name(".."), "...dat");
    }

    #[test]
    fn the_folder_sits_in_musicbees_internal_cache_beside_the_plugin_storage() {
        let backdrops = Backdrops::beside(Path::new(r"C:\MusicBee\AppData\mb_remote"));
        assert_eq!(
            *backdrops.lock_dir(),
            Some(PathBuf::from(
                r"C:\MusicBee\AppData\InternalCache\ArtistBackdrops"
            ))
        );
    }

    #[test]
    fn a_thumbnail_path_names_the_folder_musicbee_really_uses() {
        let backdrops = Backdrops::beside(Path::new(r"C:\MusicBee\AppData\mb_remote"));
        backdrops.learn_from_thumb(Path::new(r"D:\Cache\InternalCache\ArtistThumbs\Adele.png"));
        let learned = Some(PathBuf::from(r"D:\Cache\InternalCache\ArtistBackdrops"));
        assert_eq!(*backdrops.lock_dir(), learned);
        backdrops.learn_from_thumb(Path::new(r"E:\Elsewhere\Adele.png"));
        assert_eq!(
            *backdrops.lock_dir(),
            learned,
            "a path outside ArtistThumbs is not taken"
        );
    }

    #[test]
    fn an_empty_or_missing_backdrop_is_none() {
        let dir = crate::test_dir::TestDir::new("backdrops-read");
        std::fs::write(dir.join("Adele.dat"), b"jpeg").unwrap();
        std::fs::write(dir.join("Nobody.dat"), b"").unwrap();
        let backdrops = Backdrops::in_dir(dir.to_path_buf());
        assert_eq!(backdrops.read(" Adele "), Some(b"jpeg".to_vec()));
        assert_eq!(backdrops.read("Nobody"), None);
        assert_eq!(backdrops.read("Missing"), None);
        assert_eq!(backdrops.read("  "), None);
        assert_eq!(Backdrops::default().read("Adele"), None);
    }
}
