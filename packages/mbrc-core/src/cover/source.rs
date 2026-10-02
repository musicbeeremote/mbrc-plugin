//! Reading album artwork in the core, from where MusicBee says it is (#232).
//!
//! MusicBee answers where an album's picture is almost for free, while handing
//! over its bytes takes tens of milliseconds an album, one at a time under its
//! API lock. So the cover build asks where, and its workers read the picture
//! themselves: a linked image straight from its file, an embedded one from the
//! track's tags. Anything they cannot read goes back to MusicBee for the bytes,
//! so no cover ends up worse than before.

use std::path::{Path, PathBuf};

use lofty::config::ParseOptions;
use lofty::file::TaggedFileExt;
use lofty::probe::Probe;

/// MusicBee's `PictureLocations` flag for artwork embedded in the track.
const EMBED_IN_FILE: i32 = 1;

/// The `PictureLocations` flags for artwork kept in an image file of its own:
/// an organised copy, a link to the source image, or a folder thumbnail.
const LINKED: i32 = 2 | 4 | 8;

/// Where the core can read an album's artwork from.
#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Source {
    /// An image file of its own.
    File(PathBuf),
    /// The first picture in a track's tags.
    Embedded(PathBuf),
}

impl Source {
    /// Where to read the artwork MusicBee located for `track`.
    ///
    /// `None` when the core should not try: MusicBee knows of no artwork (it is
    /// asked for the bytes, so a "none" is its answer and not a guess), or the
    /// location is one the core does not understand.
    pub fn locate(track: &str, location: i32, url: &str) -> Option<Self> {
        if location & LINKED != 0 && !url.is_empty() {
            Some(Self::File(PathBuf::from(url)))
        } else if location & EMBED_IN_FILE != 0 && !track.is_empty() {
            Some(Self::Embedded(PathBuf::from(track)))
        } else {
            None
        }
    }

    /// Reads the artwork's bytes.
    ///
    /// # Errors
    /// The file cannot be read, its tags cannot be parsed, or they hold no
    /// picture; the caller then asks MusicBee instead.
    pub fn read(&self) -> Result<Vec<u8>, String> {
        match self {
            Self::File(path) => std::fs::read(path).map_err(|e| format!("read image file: {e}")),
            Self::Embedded(path) => first_picture(path),
        }
    }
}

/// The first picture in a track's tags, primary tag first.
///
/// MusicBee is asked for picture index 0; the core takes the first picture the
/// file carries, which the build's parity check compares against MusicBee's.
fn first_picture(path: &Path) -> Result<Vec<u8>, String> {
    let tagged = Probe::open(path)
        .map_err(|e| format!("open track: {e}"))?
        .options(ParseOptions::new().read_properties(false))
        .read()
        .map_err(|e| format!("read tags: {e}"))?;
    tagged
        .primary_tag()
        .into_iter()
        .chain(tagged.tags())
        .flat_map(|tag| tag.pictures())
        .next()
        .map(|picture| picture.data().to_vec())
        .ok_or_else(|| "the tags hold no picture".to_owned())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn linked_artwork_is_read_from_its_own_file() {
        assert_eq!(
            Source::locate("C:/m/01.mp3", 2, "C:/m/cover.jpg"),
            Some(Source::File(PathBuf::from("C:/m/cover.jpg")))
        );
        assert_eq!(
            Source::locate("C:/m/01.mp3", 4 | 1, "C:/m/folder.jpg"),
            Some(Source::File(PathBuf::from("C:/m/folder.jpg"))),
            "a linked image wins over an embedded one, as MusicBee reported it"
        );
    }

    #[test]
    fn embedded_artwork_is_read_from_the_track() {
        assert_eq!(
            Source::locate("C:/m/01.mp3", 1, ""),
            Some(Source::Embedded(PathBuf::from("C:/m/01.mp3")))
        );
    }

    #[test]
    fn no_known_location_leaves_it_to_musicbee() {
        assert_eq!(Source::locate("C:/m/01.mp3", 0, ""), None);
        assert_eq!(
            Source::locate("C:/m/01.mp3", 2, ""),
            None,
            "linked, but no path"
        );
        assert_eq!(Source::locate("", 1, ""), None);
    }

    #[test]
    fn a_missing_file_is_an_error_not_a_panic() {
        let missing = std::env::temp_dir().join("mbrc-no-such-cover.jpg");
        assert!(Source::File(missing.clone()).read().is_err());
        assert!(Source::Embedded(missing).read().is_err());
    }

    /// An MP3 carrying `picture` in an ID3v2 tag, followed by one silent frame.
    fn mp3_with_picture(name: &str, picture: &[u8]) -> PathBuf {
        use lofty::config::WriteOptions;
        use lofty::id3::v2::Id3v2Tag;
        use lofty::picture::{MimeType, Picture, PictureType};
        use lofty::tag::TagExt;

        let mut tag = Id3v2Tag::new();
        tag.insert_picture(
            Picture::unchecked(picture.to_vec())
                .pic_type(PictureType::CoverFront)
                .mime_type(MimeType::Jpeg)
                .build(),
        );
        let mut bytes = Vec::new();
        tag.dump_to(&mut bytes, WriteOptions::default()).unwrap();
        // MPEG-1 Layer III, 128 kbps, 44.1 kHz: a 417-byte frame.
        bytes.extend([0xFF, 0xFB, 0x90, 0x00]);
        bytes.extend(std::iter::repeat_n(0u8, 413));
        let path =
            std::env::temp_dir().join(format!("mbrc-source-{name}-{}.mp3", std::process::id()));
        std::fs::write(&path, bytes).unwrap();
        path
    }

    #[test]
    fn an_embedded_picture_is_read_from_the_tags() {
        let picture = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 0xFF, 0xD9];
        let path = mp3_with_picture("embedded", &picture);
        assert_eq!(Source::Embedded(path.clone()).read().unwrap(), picture);
        let _ = std::fs::remove_file(path);
    }

    #[test]
    fn a_linked_file_is_read_as_it_is() {
        let path = std::env::temp_dir().join(format!("mbrc-source-{}.jpg", std::process::id()));
        std::fs::write(&path, [0xFF, 0xD8, 0xFF, 0xD9]).unwrap();
        assert_eq!(
            Source::File(path.clone()).read().unwrap(),
            [0xFF, 0xD8, 0xFF, 0xD9]
        );
        let _ = std::fs::remove_file(path);
    }
}
