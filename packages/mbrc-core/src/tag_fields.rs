//! The tag fields a V6 client reads and edits, by a fixed key (#225).
//!
//! Clients address a field by its lowercase key, never by the name the user gave
//! it in MusicBee: names change, collide with built-in names and are localized.
//! The key maps to MusicBee's `MetaDataType` ids here, so the host matches no
//! names. Rating has ops of its own; lyrics can be written but are not listed,
//! since an editor of short fields has no use for them.
//!
//! The artists are three keys over two fields (#139): `artist` and
//! `artist_credits` are `MultiArtist`, the credited names with their roles, and
//! `artist_display` is the string MusicBee shows, which may be prose such as
//! "A feat. B" that no list could rebuild.

/// Whether a field holds several values.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Multi {
    Never,
    Always,
    /// MusicBee's API does not say whether a custom field is multi-value, so it
    /// is decided from the library: multi once any file holds a separated value.
    Inferred,
}

/// How a field's raw value is shaped on the wire.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Form {
    /// A string, or an array split on `;` for a multi-value field.
    Plain,
    /// The names a track is browsed under, from its credits: artists and guests.
    /// A write replaces them and keeps every other credit.
    Names,
    /// Every credit as `{name, role, detail?}`.
    Credits,
}

/// MusicBee's display artist, read beside the credits as their fallback.
pub const DISPLAY_ARTIST: i32 = 32;
/// MusicBee's credited artists with their roles (`MultiArtist`).
const MULTI_ARTIST: i32 = 33;

/// One editable field.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TagField {
    pub key: &'static str,
    /// MusicBee's `MetaDataType` id the field is written through.
    pub field: i32,
    /// The id it is read through. For composer this is MusicBee's list of the
    /// individual names, not the display string it formats them into.
    pub read: i32,
    /// The id whose user-facing name `tag_fields` shows.
    pub label: i32,
    /// A name `tag_fields` shows in place of MusicBee's, for a field MusicBee
    /// gives no name of its own.
    pub name: Option<&'static str>,
    pub multi: Multi,
    pub form: Form,
    /// Whether `tag_fields` offers it to an editor.
    pub listed: bool,
}

const fn field(key: &'static str, id: i32, multi: Multi) -> TagField {
    TagField {
        key,
        field: id,
        read: id,
        label: id,
        name: None,
        multi,
        form: Form::Plain,
        listed: true,
    }
}

const fn names(key: &'static str, displayed: i32, individual: i32) -> TagField {
    TagField {
        read: individual,
        ..field(key, displayed, Multi::Always)
    }
}

const fn credits(key: &'static str, form: Form, listed: bool) -> TagField {
    TagField {
        label: DISPLAY_ARTIST,
        form,
        listed,
        ..field(key, MULTI_ARTIST, Multi::Always)
    }
}

/// MusicBee names the display string "Artist" too; the splitter calls it the
/// display artist.
const fn display_artist() -> TagField {
    TagField {
        name: Some("Display artist"),
        ..field("artist_display", DISPLAY_ARTIST, Multi::Never)
    }
}

const fn unlisted(key: &'static str, id: i32) -> TagField {
    TagField {
        listed: false,
        ..field(key, id, Multi::Never)
    }
}

/// Every editable field, in the order `tag_fields` lists them.
pub const FIELDS: &[TagField] = &[
    field("title", 65, Multi::Never),
    credits("artist", Form::Names, true),
    display_artist(),
    field("album", 30, Multi::Never),
    field("album_artist", 34, Multi::Never),
    field("year", 88, Multi::Never),
    field("original_year", 175, Multi::Never),
    field("genre", 59, Multi::Always),
    names("composer", 43, 89),
    field("grouping", 61, Multi::Never),
    field("publisher", 73, Multi::Never),
    field("comment", 44, Multi::Never),
    field("mood", 64, Multi::Always),
    field("occasion", 66, Multi::Always),
    field("bpm", 41, Multi::Never),
    field("custom1", 46, Multi::Inferred),
    field("custom2", 47, Multi::Inferred),
    field("custom3", 48, Multi::Inferred),
    field("custom4", 49, Multi::Inferred),
    field("custom5", 50, Multi::Inferred),
    field("custom6", 96, Multi::Inferred),
    field("custom7", 97, Multi::Inferred),
    field("custom8", 98, Multi::Inferred),
    field("custom9", 99, Multi::Inferred),
    field("custom10", 128, Multi::Inferred),
    field("custom11", 129, Multi::Inferred),
    field("custom12", 130, Multi::Inferred),
    field("custom13", 131, Multi::Inferred),
    field("custom14", 132, Multi::Inferred),
    field("custom15", 133, Multi::Inferred),
    field("custom16", 134, Multi::Inferred),
    credits("artist_credits", Form::Credits, false),
    unlisted("lyrics", 114),
];

/// The field a key names.
pub fn by_key(key: &str) -> Option<&'static TagField> {
    FIELDS.iter().find(|f| f.key == key)
}

/// Whether a column of raw values holds a separated value, which is what makes
/// an inferred field multi-value.
pub fn holds_separated<'a>(values: impl IntoIterator<Item = &'a str>) -> bool {
    values.into_iter().any(|v| v.contains([';', '\0']))
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn keys_and_listed_field_ids_are_unique() {
        let listed: Vec<&TagField> = FIELDS.iter().filter(|f| f.listed).collect();
        for (i, a) in FIELDS.iter().enumerate() {
            for b in &FIELDS[i + 1..] {
                assert_ne!(a.key, b.key);
            }
        }
        for (i, a) in listed.iter().enumerate() {
            for b in &listed[i + 1..] {
                assert_ne!(a.field, b.field, "{} and {}", a.key, b.key);
            }
        }
    }

    #[test]
    fn a_key_resolves_exactly_never_by_display_name() {
        assert_eq!(by_key("custom2").map(|f| f.field), Some(47));
        assert!(by_key("Custom2").is_none());
        assert!(by_key("Genre").is_none());
        assert!(by_key("rating").is_none());
    }

    /// Writing the display field with a list stores the list as one string;
    /// only `MultiArtist` takes the credits.
    #[test]
    fn artist_names_are_read_and_written_through_the_credits() {
        let artist = by_key("artist").unwrap();
        assert_eq!(
            (artist.field, artist.read, artist.form),
            (33, 33, Form::Names)
        );
        assert_eq!(by_key("artist_display").map(|f| f.field), Some(32));
        assert_eq!(
            by_key("composer").map(|f| (f.field, f.read)),
            Some((43, 89))
        );
    }

    #[test]
    fn lyrics_and_credits_can_be_written_but_are_not_offered() {
        let lyrics = by_key("lyrics").unwrap();
        assert!(!lyrics.listed);
        assert_eq!(lyrics.multi, Multi::Never);
        assert!(!by_key("artist_credits").unwrap().listed);
    }

    #[test]
    fn a_custom_field_is_multi_value_once_a_file_separates_values() {
        assert!(!holds_separated(["8", ""]));
        assert!(holds_separated(["Bass; Cello"]));
    }
}
