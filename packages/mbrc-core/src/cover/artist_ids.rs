//! MusicBrainz artist ids from the library's own tags (#280).
//!
//! MusicBee's API has no field for `MUSICBRAINZ_ARTISTID`, so the files are read
//! with lofty. An id from a tag skips the fetcher's name search, which can only
//! answer `ambiguous` for an artist that shares its name with another.
//!
//! Picard writes the ids in the order of the recording's artist credit, so only
//! the first is known to be the lead artist's; MusicBee's own order of the other
//! names need not match it. The album artist's first id is used the same way.
//! Files that disagree give no id at all.

use std::collections::HashMap;
use std::path::Path;
use std::sync::{Mutex, Weak};

use lofty::config::ParsingMode;
use lofty::file::TaggedFileExt;
use lofty::tag::ItemKey;

use super::artists::picture_key;
use super::source::read_tags;
use crate::metadata_cache::{CachedTags, MetadataCache};
use crate::multi_value;
use crate::server_identity::is_uuid;

/// How many of an artist's files are read for an id.
const FILES_READ: usize = 3;

/// Answers the fetcher's tag lookups, each artist once per worker, since a
/// parked artist is looked up again when it comes back.
///
/// Holds the cache weakly, so the worker never keeps it alive.
pub struct TagIds {
    cache: Weak<MetadataCache>,
    answered: Mutex<HashMap<String, Option<String>>>,
}

impl TagIds {
    pub fn new(cache: Weak<MetadataCache>) -> Self {
        Self {
            cache,
            answered: Mutex::new(HashMap::new()),
        }
    }

    /// The MusicBrainz id the library's tags give `name`, a lead artist.
    pub fn get(&self, name: &str) -> Option<String> {
        let wanted = picture_key(name);
        if wanted.is_empty() {
            return None;
        }
        if let Some(known) = self.lock().get(&wanted) {
            return known.clone();
        }
        let tracks = self
            .cache
            .upgrade()?
            .first_tracks_where(|t| Lead::of(t, &wanted).is_some(), FILES_READ);
        let id = id_from_tracks(&tracks, &wanted);
        self.lock().insert(wanted, id.clone());
        id
    }

    fn lock(&self) -> std::sync::MutexGuard<'_, HashMap<String, Option<String>>> {
        self.answered.lock().unwrap_or_else(|e| e.into_inner())
    }
}

/// Which of a track's names the artist keyed `wanted` is.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Lead {
    /// The first credited name, whose id Picard writes first.
    Artist,
    AlbumArtist,
}

impl Lead {
    fn of(tags: &CachedTags, wanted: &str) -> Option<Self> {
        let names = tags.credited_names();
        if names.first().is_some_and(|n| picture_key(n) == wanted) {
            Some(Self::Artist)
        } else if picture_key(&tags.album_artist) == wanted {
            Some(Self::AlbumArtist)
        } else {
            None
        }
    }

    fn key(self) -> ItemKey {
        match self {
            Self::Artist => ItemKey::MusicBrainzArtistId,
            Self::AlbumArtist => ItemKey::MusicBrainzReleaseArtistId,
        }
    }
}

/// The one id these tracks' files agree on for `wanted`.
fn id_from_tracks(tracks: &[CachedTags], wanted: &str) -> Option<String> {
    let mut agreed: Option<String> = None;
    for track in tracks {
        let Some(lead) = Lead::of(track, wanted) else {
            continue;
        };
        let Some(id) = first_id(Path::new(&track.src), lead.key()) else {
            continue;
        };
        match &agreed {
            Some(earlier) if *earlier != id => return None,
            Some(_) => {}
            None => agreed = Some(id),
        }
    }
    agreed
}

/// The first id under `key` in a file's tags; `None` when it cannot be read or
/// has none.
fn first_id(path: &Path, key: ItemKey) -> Option<String> {
    let tagged = read_tags(path, ParsingMode::Relaxed)
        .or_else(|_| read_tags(path, ParsingMode::BestAttempt))
        .ok()?;
    tagged
        .tags()
        .iter()
        .flat_map(|t| t.get_strings(key))
        .flat_map(split_ids)
        .next()
}

/// The ids in one tag value: Picard joins several with `/` or `; ` in tags
/// that hold one value, and anything that is not an id is dropped.
fn split_ids(value: &str) -> impl Iterator<Item = String> + '_ {
    multi_value::values(value)
        .flat_map(|v| v.split('/'))
        .map(str::trim)
        .filter(|id| is_uuid(id))
        .map(str::to_ascii_lowercase)
}

#[cfg(test)]
mod tests {
    use std::path::PathBuf;

    use lofty::config::WriteOptions;
    use lofty::id3::v2::Id3v2Tag;
    use lofty::tag::{ItemValue, Tag, TagExt, TagItem, TagType};

    use super::*;

    const ELUVEITIE: &str = "8000598a-5edb-401c-8e6d-36b167feaf38";
    const ANNA: &str = "0b8f2d0e-4ea6-4c2e-b8d0-3f6a1c2b9d11";
    const OTHER: &str = "11111111-2222-3333-4444-555555555555";

    /// An MP3 whose ID3v2 tag carries `artist_ids` and `album_artist_ids`.
    fn mp3(dir: &Path, file: &str, artist_ids: &[&str], album_artist_ids: &[&str]) -> PathBuf {
        let mut tag = Tag::new(TagType::Id3v2);
        for id in artist_ids {
            tag.push(TagItem::new(
                ItemKey::MusicBrainzArtistId,
                ItemValue::Text((*id).to_owned()),
            ));
        }
        for id in album_artist_ids {
            tag.push(TagItem::new(
                ItemKey::MusicBrainzReleaseArtistId,
                ItemValue::Text((*id).to_owned()),
            ));
        }
        let mut bytes = Vec::new();
        Id3v2Tag::from(tag)
            .dump_to(&mut bytes, WriteOptions::default())
            .unwrap();
        bytes.extend(crate::cover::silent_mp3_frame());
        let path = dir.join(file);
        std::fs::write(&path, bytes).unwrap();
        path
    }

    fn track(path: &Path, artist: &str, multi_artist: &str, album_artist: &str) -> CachedTags {
        CachedTags {
            src: path.to_string_lossy().into_owned(),
            artist: artist.into(),
            multi_artist: multi_artist.into(),
            album_artist: album_artist.into(),
            ..CachedTags::default()
        }
    }

    fn id(tracks: &[CachedTags], name: &str) -> Option<String> {
        id_from_tracks(tracks, &picture_key(name))
    }

    #[test]
    fn the_first_artist_id_belongs_to_the_lead_artist() {
        let dir = crate::test_dir::TestDir::new("artist-ids-lead");
        let path = mp3(&dir, "a.mp3", &[ELUVEITIE, ANNA], &[]);
        let tracks = [track(&path, "Eluveitie feat. Anna Murphy", "", "")];
        assert_eq!(id(&tracks, "eluveitie").as_deref(), Some(ELUVEITIE));
        assert_eq!(
            id(&tracks, "Anna Murphy"),
            None,
            "an unsplit name gives no position for its guest"
        );
    }

    #[test]
    fn a_guest_gets_no_id_since_its_position_is_not_known() {
        let dir = crate::test_dir::TestDir::new("artist-ids-split");
        let path = mp3(&dir, "a.mp3", &[ELUVEITIE, ANNA], &[]);
        let split = crate::credits::encode(&[
            crate::credits::Credit::new("Eluveitie", crate::credits::Role::Artist),
            crate::credits::Credit::new("Anna Murphy", crate::credits::Role::Guest),
        ]);
        let tracks = [track(&path, "Eluveitie feat. Anna Murphy", &split, "")];
        assert_eq!(id(&tracks, "Eluveitie").as_deref(), Some(ELUVEITIE));
        assert_eq!(id(&tracks, "Anna Murphy"), None);
    }

    #[test]
    fn the_album_artist_id_is_used_for_the_album_artist() {
        let dir = crate::test_dir::TestDir::new("artist-ids-album");
        let path = mp3(&dir, "a.mp3", &[OTHER], &[ELUVEITIE]);
        let tracks = [track(&path, "Someone Else", "", "Eluveitie")];
        assert_eq!(id(&tracks, "Eluveitie").as_deref(), Some(ELUVEITIE));
    }

    #[test]
    fn files_that_disagree_give_no_id() {
        let dir = crate::test_dir::TestDir::new("artist-ids-disagree");
        let a = mp3(&dir, "a.mp3", &[ELUVEITIE], &[]);
        let b = mp3(&dir, "b.mp3", &[OTHER], &[]);
        let tracks = [
            track(&a, "Eluveitie", "", ""),
            track(&b, "Eluveitie", "", ""),
        ];
        assert_eq!(id(&tracks, "Eluveitie"), None);
    }

    #[test]
    fn an_untagged_or_missing_file_is_skipped() {
        let dir = crate::test_dir::TestDir::new("artist-ids-untagged");
        let bare = mp3(&dir, "bare.mp3", &[], &[]);
        let tagged = mp3(&dir, "tagged.mp3", &[ELUVEITIE], &[]);
        let tracks = [
            track(&dir.join("gone.mp3"), "Eluveitie", "", ""),
            track(&bare, "Eluveitie", "", ""),
            track(&tagged, "Eluveitie", "", ""),
        ];
        assert_eq!(id(&tracks, "Eluveitie").as_deref(), Some(ELUVEITIE));
    }

    #[test]
    fn ids_joined_in_one_value_are_split_and_junk_is_dropped() {
        let joined = format!("{ELUVEITIE}/{} ; not-an-id", ANNA.to_uppercase());
        assert_eq!(
            split_ids(&joined).collect::<Vec<_>>(),
            [ELUVEITIE.to_owned(), ANNA.to_owned()]
        );
    }
}
