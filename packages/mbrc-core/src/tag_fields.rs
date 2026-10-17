//! The tag fields a V6 client reads and edits, by a fixed key (#225).
//!
//! Clients address a field by its lowercase key, never by the name the user gave
//! it in MusicBee: names change, collide with built-in names and are localized.
//! The key maps to MusicBee's `MetaDataType` ids here, so the host matches no
//! names. Rating has ops of its own; lyrics can be written but are not listed,
//! since an editor of short fields has no use for them.

/// Whether a field holds several values.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum Multi {
    Never,
    Always,
    /// MusicBee's API does not say whether a custom field is multi-value, so it
    /// is decided from the library: multi once any file holds a separated value.
    Inferred,
}

/// One editable field.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct TagField {
    pub key: &'static str,
    /// MusicBee's `MetaDataType` id the field is written through.
    pub field: i32,
    /// The id it is read through. For artist and composer this is MusicBee's
    /// list of the individual names, not the display string it formats them
    /// into ("A feat. B"), which a client could not split.
    pub read: i32,
    pub multi: Multi,
    /// Whether `tag_fields` offers it to an editor.
    pub listed: bool,
}

const fn field(key: &'static str, id: i32, multi: Multi) -> TagField {
    TagField {
        key,
        field: id,
        read: id,
        multi,
        listed: true,
    }
}

const fn names(key: &'static str, displayed: i32, individual: i32) -> TagField {
    TagField {
        read: individual,
        ..field(key, displayed, Multi::Always)
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
    names("artist", 32, 33),
    field("album", 30, Multi::Never),
    field("album_artist", 34, Multi::Never),
    field("year", 88, Multi::Never),
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
    fn keys_and_field_ids_are_unique() {
        for (i, a) in FIELDS.iter().enumerate() {
            for b in &FIELDS[i + 1..] {
                assert_ne!(a.key, b.key);
                assert_ne!(a.field, b.field, "{} and {}", a.key, b.key);
                assert_ne!(a.read, b.read, "{} and {}", a.key, b.key);
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

    /// MusicBee formats several artists into one display string, which reads
    /// back as one value however they were written.
    #[test]
    fn artist_and_composer_are_read_as_individual_names() {
        assert_eq!(by_key("artist").map(|f| (f.field, f.read)), Some((32, 33)));
        assert_eq!(
            by_key("composer").map(|f| (f.field, f.read)),
            Some((43, 89))
        );
    }

    #[test]
    fn lyrics_can_be_written_but_are_not_offered() {
        let lyrics = by_key("lyrics").unwrap();
        assert!(!lyrics.listed);
        assert_eq!(lyrics.multi, Multi::Never);
    }

    #[test]
    fn a_custom_field_is_multi_value_once_a_file_separates_values() {
        assert!(!holds_separated(["8", ""]));
        assert!(holds_separated(["Bass; Cello"]));
    }
}
