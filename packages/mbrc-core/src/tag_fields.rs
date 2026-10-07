//! The tag fields a V6 client reads and edits, by a fixed key (#225).
//!
//! Clients address a field by its lowercase key, never by the name the user gave
//! it in MusicBee: names change, collide with built-in names and are localized.
//! The key maps to MusicBee's `MetaDataType` id here, so the host matches no
//! names. Lyrics and rating are left out because they have ops of their own.

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
    /// MusicBee's `MetaDataType` id.
    pub field: i32,
    pub multi: Multi,
}

const fn field(key: &'static str, field: i32, multi: Multi) -> TagField {
    TagField { key, field, multi }
}

/// Every editable field, in the order `tag_fields` lists them.
pub const FIELDS: &[TagField] = &[
    field("title", 65, Multi::Never),
    field("artist", 32, Multi::Always),
    field("album", 30, Multi::Never),
    field("album_artist", 34, Multi::Never),
    field("year", 88, Multi::Never),
    field("genre", 59, Multi::Always),
    field("composer", 43, Multi::Always),
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
];

/// The field a key names.
pub fn by_key(key: &str) -> Option<&'static TagField> {
    FIELDS.iter().find(|f| f.key == key)
}

/// Whether a column of raw values holds a separated value, which is what makes
/// an inferred field multi-value.
pub fn holds_separated(values: &[String]) -> bool {
    values.iter().any(|v| v.contains([';', '\0']))
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
            }
        }
    }

    #[test]
    fn a_key_resolves_exactly_never_by_display_name() {
        assert_eq!(by_key("custom2").map(|f| f.field), Some(47));
        assert!(by_key("Custom2").is_none());
        assert!(by_key("Genre").is_none());
        assert!(by_key("lyrics").is_none());
    }

    #[test]
    fn a_custom_field_is_multi_value_once_a_file_separates_values() {
        assert!(!holds_separated(&["8".into(), String::new()]));
        assert!(holds_separated(&["Bass; Cello".into()]));
    }
}
