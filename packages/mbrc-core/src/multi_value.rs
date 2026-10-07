//! How a multi-value tag such as genre splits into its values.
//!
//! MusicBee shows a multi-value field joined with `; ` and a tag written by
//! another program may use `;` or a NUL. The genre list, the genre filters and
//! distinct tag values all split the same way, so they agree on what one value
//! is. Values are compared ignoring ASCII case, as MusicBee's own lookups do.

use std::collections::HashMap;

/// The values of a multi-value field, trimmed, without empties.
pub fn values(raw: &str) -> impl Iterator<Item = &str> {
    raw.split([';', '\0'])
        .map(str::trim)
        .filter(|v| !v.is_empty())
}

/// Whether one of the field's values is `value`.
///
/// The empty name matches a field with no value, which is how the untagged
/// group is browsed.
pub fn holds(raw: &str, value: &str) -> bool {
    let value = value.trim();
    if value.is_empty() {
        return values(raw).next().is_none();
    }
    values(raw).any(|v| v.eq_ignore_ascii_case(value))
}

/// Each distinct value across `fields`, with how many fields hold it, by name.
///
/// Values differing only in case are one, spelled the way most fields spell
/// it. A field naming a value twice counts once, and a field with no value
/// counts under the empty name.
pub fn counts<'a>(fields: impl IntoIterator<Item = &'a str>) -> Vec<(String, u32)> {
    let mut by_key: HashMap<String, (u32, HashMap<&'a str, u32>)> = HashMap::new();
    for raw in fields {
        let mut seen: Vec<String> = Vec::new();
        let mut field_values: Vec<&str> = values(raw).collect();
        if field_values.is_empty() {
            field_values.push("");
        }
        for value in field_values {
            let key = value.to_ascii_lowercase();
            if seen.contains(&key) {
                continue;
            }
            let entry = by_key.entry(key.clone()).or_default();
            entry.0 += 1;
            *entry.1.entry(value).or_default() += 1;
            seen.push(key);
        }
    }
    let mut out: Vec<(String, u32)> = by_key
        .into_values()
        .map(|(count, spellings)| {
            let name = spellings
                .into_iter()
                .max_by(|(a, x), (b, y)| x.cmp(y).then_with(|| b.cmp(a)))
                .map(|(name, _)| name.to_string())
                .unwrap_or_default();
            (name, count)
        })
        .collect();
    out.sort_by(|(a, _), (b, _)| {
        a.to_ascii_lowercase()
            .cmp(&b.to_ascii_lowercase())
            .then_with(|| a.cmp(b))
    });
    out
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn splits_on_the_separators_musicbee_and_other_taggers_use() {
        let split: Vec<&str> = values("Gothic Metal; Power Metal;Metal\0Folk").collect();
        assert_eq!(split, ["Gothic Metal", "Power Metal", "Metal", "Folk"]);
        assert_eq!(values(" ; ;").count(), 0);
    }

    #[test]
    fn a_field_holds_each_of_its_values_not_only_the_first() {
        let genre = "Gothic Metal; Power Metal; Metal";
        assert!(holds(genre, "Gothic Metal"));
        assert!(holds(genre, "power metal"));
        assert!(holds(genre, "Metal"));
        assert!(!holds(genre, "Gothic"));
        assert!(!holds(genre, ""));
        assert!(holds("", ""));
    }

    #[test]
    fn a_file_with_two_genres_is_counted_under_both() {
        let out = counts(["Rock; Jazz", "Rock", "Pop"]);
        assert_eq!(
            out,
            [("Jazz".into(), 1), ("Pop".into(), 1), ("Rock".into(), 2)]
        );
    }

    #[test]
    fn values_differing_in_case_are_one_spelled_as_most_spell_it() {
        let out = counts(["Trance", "trance", "Trance", "Trance; trance"]);
        assert_eq!(out, [("Trance".into(), 4)]);
    }

    #[test]
    fn a_field_with_no_value_counts_under_the_empty_name() {
        assert_eq!(counts(["", "Rock"]), [("".into(), 1), ("Rock".into(), 1)]);
    }
}
