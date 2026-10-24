//! V6 tag editing by field (#225, #229).
//!
//! - `tag_fields {}`: every editable field, the user's name for it, and whether
//!   it holds several values
//! - `now_playing_tags { keys? }`: the playing track's values
//! - `tag_values { key }`: every distinct value of one field across the library
//! - `now_playing_set_tag { path, key, value }`: the write, refused with
//!   `stale_track` when `path` is no longer what plays
//!
//! A multi-value field is always a JSON array and a single-value one a string,
//! so a client never splits. Custom fields are judged multi-value from one read
//! of every track's fields after a start or a library change; an edit patches
//! its own track's row. `artist` and `artist_credits` are decoded from
//! MusicBee's role-tagged credits ([`crate::credits`]).

use std::sync::Arc;
use std::time::Instant;

use serde_json::{Map, Value, json};

use mbrc_wire::v6::ErrorCode;

use super::{OpResult, V6Error, internal, req_str};
use crate::credits::{self, Credit, Role};
use crate::metadata_cache::{MetadataCache, TagRows};
use crate::multi_value;
use crate::providers::Providers;
use crate::tag_fields::{self, DISPLAY_ARTIST, FIELDS, Form, Multi, TagField};

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &[
    "tag_fields",
    "tag_values",
    "now_playing_tags",
    "now_playing_set_tag",
];

/// Paths read per host call while reading every track's fields.
const ROW_BATCH: usize = 500;

/// Dispatch a tag op. `None` if `op` is not in this domain.
pub fn dispatch(
    op: &str,
    data: &Value,
    p: &dyn Providers,
    cache: Option<&MetadataCache>,
) -> Option<OpResult> {
    Some(match op {
        "tag_fields" => fields(p, cache),
        "tag_values" => values(data, p, cache),
        "now_playing_tags" => now_playing_tags(data, p, cache),
        "now_playing_set_tag" => set_tag(data, p, cache),
        _ => return None,
    })
}

fn listed() -> impl Iterator<Item = &'static TagField> {
    FIELDS.iter().filter(|f| f.listed)
}

fn fields(p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let names = p
        .field_names(listed().map(|f| f.label).collect())
        .map_err(internal)?;
    let rows = rows(p, cache)?;
    let fields: Vec<Value> = listed()
        .zip(names)
        .map(|(f, name)| {
            let name = f.name.unwrap_or(name.trim());
            json!({ "key": f.key, "name": name, "multi_value": is_multi(f, &rows) })
        })
        .collect();
    Ok(json!({ "fields": fields }))
}

fn values(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let field = field_for(data, "key")?;
    let rows = rows(p, cache)?;
    let split = is_multi(field, &rows);
    let column: Vec<String> = match field.form {
        Form::Plain => column(&rows, field).map(str::to_owned).collect(),
        Form::Names | Form::Credits => names_column(&rows, field),
    };
    let values: Vec<Value> = multi_value::distinct(column.iter().map(String::as_str), split)
        .into_iter()
        .map(|(value, count)| json!({ "value": value, "count": count }))
        .collect();
    Ok(json!({ "key": field.key, "values": values }))
}

fn now_playing_tags(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let wanted: Vec<&TagField> = match data.get("keys") {
        None | Some(Value::Null) => listed().collect(),
        Some(Value::Array(keys)) => keys
            .iter()
            .map(|k| {
                k.as_str()
                    .and_then(tag_fields::by_key)
                    .ok_or_else(|| unknown_key("keys", &k.to_string()))
            })
            .collect::<Result<_, _>>()?,
        Some(_) => {
            return Err(V6Error::field(
                ErrorCode::InvalidField,
                "keys",
                "keys must be an array of field keys",
            ));
        }
    };
    let mut ids: Vec<i32> = wanted.iter().map(|f| f.read).collect();
    ids.push(DISPLAY_ARTIST);
    let read = p.now_playing_tags(ids).map_err(internal)?;
    if read.path.is_empty() {
        return Ok(json!({ "path": null, "tags": {} }));
    }
    let display = read.values.get(wanted.len()).map_or("", String::as_str);
    let rows = if wanted.iter().any(|f| f.multi == Multi::Inferred) {
        rows(p, cache)?
    } else {
        Arc::default()
    };
    let mut tags = Map::new();
    for (field, raw) in wanted.iter().zip(&read.values) {
        tags.insert(
            field.key.into(),
            shaped_field(field, raw, display, is_multi(field, &rows)),
        );
    }
    Ok(json!({ "path": read.path, "tags": tags }))
}

fn set_tag(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let path = req_str(data, "path")?;
    let field = field_for(data, "key")?;
    let (stored, as_list) = match field.form {
        Form::Plain => stored_value(field, data.get("value"))?,
        Form::Names => (stored_names(p, field, data.get("value"))?, true),
        Form::Credits => (stored_credits(data.get("value"))?, true),
    };

    let result = p
        .write_now_playing_tag(path.to_owned(), field.field, field.read, stored.clone())
        .map_err(internal)?;
    match result.outcome.as_str() {
        "written" => {}
        "stale_track" => {
            return Err(V6Error::new(
                ErrorCode::StaleTrack,
                format!("{path} is no longer the playing track; nothing was written"),
            ));
        }
        _ => {
            return Err(V6Error::new(ErrorCode::Unavailable, result.reason));
        }
    }
    refresh_rows(p, cache, &[path.to_owned()]);
    if refused(&stored, &result.value, &result.previous) {
        return Err(V6Error::new(
            ErrorCode::Unavailable,
            format!(
                "MusicBee accepted {} but kept {:?}; a custom field holds a value only once it is set up in MusicBee's tag preferences",
                field.key, result.value
            ),
        ));
    }
    let value = shaped_field(field, &result.value, "", as_list);
    Ok(json!({ "path": path, "key": field.key, "value": value }))
}

/// The credits stored for a new list of names: the names replace the artists
/// and guests, and every other credit is kept.
///
/// The file is read first, since the write replaces the whole credit list. A
/// track nobody split counts its display artist as its one artist, and the
/// names it was shown, sent back unchanged, keep the stored value as it was.
fn stored_names(
    p: &dyn Providers,
    field: &TagField,
    value: Option<&Value>,
) -> Result<String, V6Error> {
    let names = string_list(field, value)?;
    let read = p
        .now_playing_tags(vec![field.read, DISPLAY_ARTIST])
        .map_err(internal)?;
    let raw = read.values.first().map_or("", String::as_str);
    let display = read.values.get(1).map_or("", String::as_str);
    let current = credits::or_display(raw, display);
    if credits::lead_names(&current) == names {
        return Ok(raw.to_owned());
    }
    Ok(credits::encode(&credits::with_lead_names(&current, &names)))
}

/// The credits stored for a full list of `{name, role, detail?}`.
fn stored_credits(value: Option<&Value>) -> Result<String, V6Error> {
    let wrong = || {
        V6Error::field(
            ErrorCode::InvalidField,
            "value",
            "artist_credits takes an array of {name, role, detail?}, role one of artist, guest, performer, remixer",
        )
    };
    let Some(Value::Array(items)) = value else {
        return Err(match value {
            None => missing_value(),
            Some(_) => wrong(),
        });
    };
    let parsed = items
        .iter()
        .map(|item| {
            let name = item.get("name").and_then(Value::as_str).ok_or_else(wrong)?;
            let role = item
                .get("role")
                .and_then(Value::as_str)
                .and_then(Role::parse)
                .ok_or_else(wrong)?;
            let detail = match item.get("detail") {
                None | Some(Value::Null) => None,
                Some(Value::String(d)) => Some(d.trim().to_owned()),
                Some(_) => return Err(wrong()),
            };
            Ok(Credit {
                name: name.trim().to_owned(),
                role,
                detail,
            })
        })
        .collect::<Result<Vec<Credit>, V6Error>>()?;
    let kept: Vec<Credit> = parsed.into_iter().filter(|c| !c.name.is_empty()).collect();
    Ok(credits::encode(&kept))
}

/// An array of strings, trimmed, without empties.
fn string_list<'a>(field: &TagField, value: Option<&'a Value>) -> Result<Vec<&'a str>, V6Error> {
    let wrong = || {
        V6Error::field(
            ErrorCode::InvalidField,
            "value",
            format!("{} takes an array of strings", field.key),
        )
    };
    match value {
        None => Err(missing_value()),
        Some(Value::Array(items)) => items
            .iter()
            .map(|item| item.as_str().map(str::trim).ok_or_else(wrong))
            .filter(|item| !matches!(item, Ok("")))
            .collect(),
        Some(_) => Err(wrong()),
    }
}

fn missing_value() -> V6Error {
    V6Error::field(
        ErrorCode::MissingField,
        "value",
        "value is required; send \"\" or [] to clear the field",
    )
}

/// Whether MusicBee kept the field as it was although another value was sent.
///
/// MusicBee answers a write it will not keep with success, and only the reads
/// before and after tell. A value it stores in its own form (a date read back
/// as its year) has still changed the field, so it is not a refusal. Compared
/// value by value, so MusicBee joining them its own way is no difference.
fn refused(sent: &str, read_back: &str, previous: &str) -> bool {
    let same = |a: &str, b: &str| multi_value::values(a).eq(multi_value::values(b));
    !same(sent, read_back) && same(read_back, previous)
}

/// The string MusicBee stores for a written value, and whether the value is a
/// list. A multi-value field takes an array, a single-value one a string, and
/// a custom field either, since a file may be the first to give it a second
/// value.
fn stored_value(field: &TagField, value: Option<&Value>) -> Result<(String, bool), V6Error> {
    let wrong = |expected: &str| {
        V6Error::field(
            ErrorCode::InvalidField,
            "value",
            format!("{} takes {expected}", field.key),
        )
    };
    match (value, field.multi) {
        (None, _) => Err(missing_value()),
        (Some(Value::String(s)), Multi::Never | Multi::Inferred) => Ok((s.clone(), false)),
        (Some(Value::Array(items)), Multi::Always | Multi::Inferred) => {
            let parts = items
                .iter()
                .map(|item| {
                    item.as_str()
                        .map(str::trim)
                        .ok_or_else(|| wrong("an array of strings"))
                })
                .collect::<Result<Vec<&str>, _>>()?;
            let kept: Vec<&str> = parts.into_iter().filter(|s| !s.is_empty()).collect();
            Ok((kept.join(multi_value::SEPARATOR), true))
        }
        (Some(_), Multi::Always) => Err(wrong("an array of strings")),
        (Some(_), _) => Err(wrong("a string")),
    }
}

/// A field's raw value as the wire carries it. `display` is the display artist,
/// which names a track nobody split.
fn shaped_field(field: &TagField, raw: &str, display: &str, multi: bool) -> Value {
    match field.form {
        Form::Plain => shaped(raw, multi),
        Form::Names => json!(credits::lead_names(&credits::or_display(raw, display))),
        Form::Credits => {
            let all = credits::or_display(raw, display);
            json!(all.iter().map(Credit::to_json).collect::<Vec<Value>>())
        }
    }
}

/// A raw value as the wire carries it: split into an array for a multi-value
/// field, as stored otherwise.
fn shaped(raw: &str, multi: bool) -> Value {
    if multi {
        json!(multi_value::list(raw))
    } else {
        json!(raw)
    }
}

fn is_multi(field: &TagField, rows: &TagRows) -> bool {
    match field.multi {
        Multi::Never => false,
        Multi::Always => true,
        Multi::Inferred => tag_fields::holds_separated(column(rows, field)),
    }
}

/// One field's value in every row.
fn column<'a>(rows: &'a TagRows, field: &TagField) -> impl Iterator<Item = &'a str> + 'a {
    let at = FIELDS.iter().position(|f| f.key == field.key);
    rows.values()
        .filter_map(move |row| at.and_then(|i| row.get(i)))
        .map(String::as_str)
}

/// The names each row's credits are browsed under, one NUL-joined value per row.
fn names_column(rows: &TagRows, field: &TagField) -> Vec<String> {
    let at = FIELDS.iter().position(|f| f.key == field.key);
    let display_at = FIELDS.iter().position(|f| f.read == DISPLAY_ARTIST);
    let cell = |row: &Vec<String>, i: Option<usize>| -> String {
        i.and_then(|i| row.get(i)).cloned().unwrap_or_default()
    };
    rows.values()
        .map(|row| {
            let credits = credits::or_display(&cell(row, at), &cell(row, display_at));
            credits::lead_names(&credits).join("\0")
        })
        .collect()
}

fn field_for(data: &Value, name: &str) -> Result<&'static TagField, V6Error> {
    let key = req_str(data, name)?;
    tag_fields::by_key(key).ok_or_else(|| unknown_key(name, key))
}

fn unknown_key(field: &str, key: &str) -> V6Error {
    V6Error::field(
        ErrorCode::InvalidField,
        field,
        format!("unknown tag key {key}; tag_fields lists them"),
    )
}

fn read_ids() -> Vec<i32> {
    FIELDS.iter().map(|f| f.read).collect()
}

/// Every track's tag-editing fields by path, kept by the cache once read.
fn rows(p: &dyn Providers, cache: Option<&MetadataCache>) -> Result<Arc<TagRows>, V6Error> {
    let read = || read_rows(p, cache);
    match cache {
        Some(c) => c.tag_rows_or_read(read),
        None => read().map(Arc::new),
    }
}

fn read_rows(p: &dyn Providers, cache: Option<&MetadataCache>) -> Result<TagRows, V6Error> {
    let started = Instant::now();
    let paths = match cache {
        Some(c) if c.track_count() > 0 => {
            c.track_page_paths(0, i32::try_from(c.track_count()).unwrap_or(i32::MAX))
        }
        _ => p.track_paths().map_err(internal)?,
    };
    let ids = read_ids();
    let mut rows = TagRows::with_capacity(paths.len());
    for batch in paths.chunks(ROW_BATCH) {
        for row in p
            .tags_for_paths(ids.clone(), batch.to_vec())
            .map_err(internal)?
        {
            rows.insert(row.path, row.values);
        }
    }
    tracing::info!(
        tracks = rows.len(),
        ms = started.elapsed().as_millis() as u64,
        "tag fields read for every track"
    );
    Ok(rows)
}

/// Re-reads the kept rows of `paths` after an edit, in one host call.
///
/// Nothing is read while no rows are kept: the next full read sees the edit.
pub(crate) fn refresh_rows(p: &dyn Providers, cache: Option<&MetadataCache>, paths: &[String]) {
    let Some(cache) = cache.filter(|c| c.has_tag_rows()) else {
        return;
    };
    if paths.is_empty() {
        return;
    }
    match p.tags_for_paths(read_ids(), paths.to_vec()) {
        Ok(read) => cache.patch_tag_rows(read.into_iter().map(|r| (r.path, r.values)).collect()),
        Err(e) => tracing::debug!(error = %e, "tag rows of edited tracks not refreshed"),
    }
}

#[cfg(test)]
mod tests {
    use std::collections::HashMap;

    use super::*;
    use crate::protocol::messages::TrackInfo;
    use crate::providers::MockProviders;

    const GENRE: i32 = 59;
    const CUSTOM1: i32 = 46;
    const CUSTOM2: i32 = 47;

    /// Two files: the playing one with two genres, a score and two
    /// instruments, and another with one instrument in another case.
    fn library() -> MockProviders {
        let file = |pairs: &[(i32, &str)]| -> HashMap<i32, String> {
            pairs.iter().map(|(f, v)| (*f, (*v).to_string())).collect()
        };
        let m = MockProviders {
            track_info: TrackInfo {
                path: "/a.flac".into(),
                ..Default::default()
            },
            track_paths: vec!["/a.flac".into(), "/b.flac".into()],
            field_names: HashMap::from([
                (CUSTOM1, "Energy".into()),
                (CUSTOM2, "Instruments".into()),
            ]),
            ..Default::default()
        };
        m.file_tags.lock().unwrap().extend([
            (
                "/a.flac".to_string(),
                file(&[
                    (GENRE, "Rock; Jazz"),
                    (CUSTOM1, "8"),
                    (CUSTOM2, "Bass; Cello"),
                ]),
            ),
            (
                "/b.flac".to_string(),
                file(&[(GENRE, "Rock"), (CUSTOM2, "bass")]),
            ),
        ]);
        m
    }

    fn run(op: &str, data: Value, m: &MockProviders) -> OpResult {
        dispatch(op, &data, m, None).unwrap()
    }

    fn field<'a>(out: &'a Value, key: &str) -> &'a Value {
        out["fields"]
            .as_array()
            .unwrap()
            .iter()
            .find(|f| f["key"] == key)
            .unwrap()
    }

    #[test]
    fn tag_fields_names_every_key_and_infers_custom_multi_value() {
        let out = run("tag_fields", json!({}), &library()).unwrap();
        assert_eq!(out["fields"].as_array().unwrap().len(), listed().count());
        assert_eq!(field(&out, "custom1")["name"], "Energy");
        assert_eq!(field(&out, "custom1")["multi_value"], false);
        assert_eq!(field(&out, "custom2")["name"], "Instruments");
        assert_eq!(field(&out, "custom2")["multi_value"], true);
        assert_eq!(field(&out, "genre")["multi_value"], true);
        assert_eq!(field(&out, "title")["multi_value"], false);
    }

    /// MusicBee calls both artist fields "Artist"; an editor showing two of
    /// them could not tell which is which.
    #[test]
    fn the_display_artist_has_a_name_of_its_own() {
        let mut m = library();
        m.field_names.extend([(32, "Artist".to_string())]);
        let out = run("tag_fields", json!({}), &m).unwrap();
        assert_eq!(field(&out, "artist")["name"], "Artist");
        assert_eq!(field(&out, "artist_display")["name"], "Display artist");
    }

    #[test]
    fn tag_values_splits_counts_and_keeps_case_apart() {
        let out = run("tag_values", json!({ "key": "custom2" }), &library()).unwrap();
        assert_eq!(out["key"], "custom2");
        assert_eq!(
            out["values"],
            json!([
                { "value": "Bass", "count": 1 },
                { "value": "Cello", "count": 1 },
                { "value": "bass", "count": 1 },
            ])
        );
        let genres = run("tag_values", json!({ "key": "genre" }), &library()).unwrap();
        assert_eq!(genres["values"][1], json!({ "value": "Rock", "count": 2 }));
    }

    #[test]
    fn an_unknown_key_is_an_invalid_field() {
        for (op, data, name) in [
            ("tag_values", json!({ "key": "Genre" }), "key"),
            (
                "now_playing_tags",
                json!({ "keys": ["genre", "rating"] }),
                "keys",
            ),
            (
                "now_playing_set_tag",
                json!({ "path": "/a.flac", "key": "bitrate", "value": "x" }),
                "key",
            ),
        ] {
            let err = run(op, data, &library()).unwrap_err();
            assert_eq!(err.code, ErrorCode::InvalidField, "{op}");
            assert_eq!(err.field.as_deref(), Some(name), "{op}");
        }
    }

    #[test]
    fn now_playing_tags_sends_arrays_for_multi_value_fields_only() {
        let out = run(
            "now_playing_tags",
            json!({ "keys": ["genre", "custom1", "custom2", "title"] }),
            &library(),
        )
        .unwrap();
        assert_eq!(out["path"], "/a.flac");
        assert_eq!(out["tags"]["genre"], json!(["Rock", "Jazz"]));
        assert_eq!(out["tags"]["custom1"], "8");
        assert_eq!(out["tags"]["custom2"], json!(["Bass", "Cello"]));
        assert_eq!(out["tags"]["title"], "");
    }

    #[test]
    fn now_playing_tags_with_nothing_playing_is_a_null_path() {
        let out = run("now_playing_tags", json!({}), &MockProviders::default()).unwrap();
        assert_eq!(out, json!({ "path": null, "tags": {} }));
    }

    #[test]
    fn a_multi_value_write_is_stored_joined_and_read_back_as_an_array() {
        let m = library();
        let out = run(
            "now_playing_set_tag",
            json!({ "path": "/a.flac", "key": "genre", "value": ["Rock", " Blues ", ""] }),
            &m,
        )
        .unwrap();
        assert!(
            m.recorded()
                .contains(&"write_now_playing_tag(59,Rock; Blues)".to_string())
        );
        assert_eq!(
            out,
            json!({ "path": "/a.flac", "key": "genre", "value": ["Rock", "Blues"] })
        );
    }

    #[test]
    fn a_value_of_the_wrong_shape_writes_nothing() {
        let m = library();
        for (key, value) in [
            ("genre", json!("Rock")),
            ("title", json!(["A"])),
            ("custom1", json!(8)),
        ] {
            let err = run(
                "now_playing_set_tag",
                json!({ "path": "/a.flac", "key": key, "value": value }),
                &m,
            )
            .unwrap_err();
            assert_eq!(err.code, ErrorCode::InvalidField, "{key}");
            assert_eq!(err.field.as_deref(), Some("value"));
        }
        assert!(
            !m.recorded()
                .iter()
                .any(|c| c.starts_with("write_now_playing_tag"))
        );
    }

    /// Seen on a playing file with a pending edit in MusicBee: the write
    /// reports success and the read-back is the value it already had.
    #[test]
    fn a_write_musicbee_answered_with_its_old_value_is_unavailable() {
        let mut m = library();
        m.dropped_fields = vec![CUSTOM2];
        let err = run(
            "now_playing_set_tag",
            json!({ "path": "/a.flac", "key": "custom2", "value": ["Viola"] }),
            &m,
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::Unavailable);
        assert!(err.message.contains("Bass; Cello"), "{}", err.message);
    }

    /// A refusal is the field left as it was. A value MusicBee stores in its
    /// own form, or joins its own way, has still been written.
    #[test]
    fn only_a_field_left_as_it_was_is_a_refusal() {
        assert!(refused("New title", "Old title", "Old title"));
        assert!(refused("x", "", ""));
        assert!(
            !refused("2020-05-01", "2020", "2005"),
            "a year MusicBee normalised"
        );
        assert!(!refused("Rock; Jazz", "Rock;Jazz", "Pop"));
        assert!(
            !refused("Same", "Same", "Same"),
            "a write of the value it had"
        );
    }

    /// MusicBee accepts a value for a custom field it has no tag for, and keeps
    /// nothing; the read-back is what tells.
    #[test]
    fn a_value_musicbee_did_not_keep_is_unavailable() {
        let mut m = library();
        m.dropped_fields = vec![48];
        let err = run(
            "now_playing_set_tag",
            json!({ "path": "/a.flac", "key": "custom3", "value": "x" }),
            &m,
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::Unavailable);
        assert!(err.message.contains("custom3"), "{}", err.message);

        let cleared = run(
            "now_playing_set_tag",
            json!({ "path": "/a.flac", "key": "custom3", "value": "" }),
            &m,
        );
        assert!(cleared.is_ok(), "clearing keeps nothing by design");
    }

    /// The track changed between the user opening the editor and saving.
    #[test]
    fn a_write_naming_a_track_no_longer_playing_is_stale() {
        let m = library();
        let err = run(
            "now_playing_set_tag",
            json!({ "path": "/b.flac", "key": "title", "value": "New" }),
            &m,
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::StaleTrack);
        assert_eq!(m.file_tags.lock().unwrap()["/b.flac"].get(&65), None);
    }

    #[test]
    fn a_value_written_shows_in_the_next_tag_values() {
        use crate::store::Db;

        let dir = std::env::temp_dir().join("mbrc-v6-tag-values-after-write");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(&[], 1);
        cache.replace_track_index(&["/a.flac".into(), "/b.flac".into()]);
        let m = library();
        let values = |m: &MockProviders| {
            dispatch("tag_values", &json!({ "key": "custom2" }), m, Some(&cache))
                .unwrap()
                .unwrap()["values"]
                .clone()
        };
        assert!(!values(&m).to_string().contains("Cello Bow"));

        dispatch(
            "now_playing_set_tag",
            &json!({ "path": "/a.flac", "key": "custom2", "value": ["Cello Bow"] }),
            &m,
            Some(&cache),
        )
        .unwrap()
        .unwrap();
        assert!(values(&m).to_string().contains("Cello Bow"));
    }

    fn cache_of(name: &str, paths: &[&str]) -> MetadataCache {
        use crate::store::Db;

        let dir = std::env::temp_dir().join(name);
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(&[], 1);
        cache.replace_track_index(&paths.iter().map(|p| p.to_string()).collect::<Vec<_>>());
        cache
    }

    /// Reading every track again for each saved field cost a full library
    /// scan per keystroke-commit on a large library.
    #[test]
    fn an_edit_patches_its_row_without_reading_every_track_again() {
        let cache = cache_of("mbrc-v6-tag-rows-patch", &["/a.flac", "/b.flac"]);
        let m = library();
        let full_reads = |m: &MockProviders| {
            m.recorded()
                .iter()
                .filter(|c| *c == "tags_for_paths(2)")
                .count()
        };
        let op =
            |name: &str, data: Value| dispatch(name, &data, &m, Some(&cache)).unwrap().unwrap();

        op("tag_fields", json!({}));
        op(
            "now_playing_set_tag",
            json!({ "path": "/a.flac", "key": "custom2", "value": ["Viola"] }),
        );
        let values = op("tag_values", json!({ "key": "custom2" }));
        op("now_playing_tags", json!({}));

        assert_eq!(full_reads(&m), 1, "{:?}", m.recorded());
        assert!(values.to_string().contains("Viola"), "{values}");
    }

    /// Lyrics have no other way to be written, and no place in a field list.
    #[test]
    fn lyrics_can_be_written_but_are_not_listed() {
        let m = library();
        let out = run(
            "now_playing_set_tag",
            json!({ "path": "/a.flac", "key": "lyrics", "value": "La la" }),
            &m,
        )
        .unwrap();
        assert_eq!(out["value"], "La la");
        let fields = run("tag_fields", json!({}), &m).unwrap();
        assert!(!fields.to_string().contains("\"lyrics\""));
        let tags = run("now_playing_tags", json!({}), &m).unwrap();
        assert!(tags["tags"].get("lyrics").is_none());
    }

    /// The playing file split as MusicBee's splitter stores it: an artist, a
    /// guest and a performer credit.
    fn split_library() -> MockProviders {
        let m = library();
        m.file_tags
            .lock()
            .unwrap()
            .get_mut("/a.flac")
            .unwrap()
            .extend([
                (32, "A feat. B".to_string()),
                (33, "A\0\u{1}B\0\u{2}C (drums)\0".to_string()),
            ]);
        m
    }

    /// MusicBee formats several artists into one display string; the names
    /// come from its credits, without their role markers or the performers.
    #[test]
    fn artist_is_read_as_the_names_on_the_track() {
        let out = run(
            "now_playing_tags",
            json!({ "keys": ["artist", "artist_display", "artist_credits"] }),
            &split_library(),
        )
        .unwrap();
        assert_eq!(out["tags"]["artist"], json!(["A", "B"]));
        assert_eq!(out["tags"]["artist_display"], "A feat. B");
        assert_eq!(
            out["tags"]["artist_credits"],
            json!([
                { "name": "A", "role": "artist" },
                { "name": "B", "role": "guest" },
                { "name": "C", "role": "performer", "detail": "drums" },
            ])
        );
    }

    #[test]
    fn a_track_nobody_split_is_named_by_its_display_artist() {
        let m = library();
        m.file_tags
            .lock()
            .unwrap()
            .get_mut("/a.flac")
            .unwrap()
            .insert(32, "A feat. B".to_string());
        let out = run("now_playing_tags", json!({ "keys": ["artist"] }), &m).unwrap();
        assert_eq!(out["tags"]["artist"], json!(["A feat. B"]));
    }

    /// An editor that only knows the flat list of names must not turn a guest
    /// into a lead artist or drop the performers.
    #[test]
    fn writing_the_names_keeps_every_role() {
        let m = split_library();
        let out = run(
            "now_playing_set_tag",
            json!({ "path": "/a.flac", "key": "artist", "value": ["A", "B", "E"] }),
            &m,
        )
        .unwrap();
        assert!(
            m.recorded()
                .contains(&"write_now_playing_tag(33,A\0\u{1}B\0E\0\u{2}C (drums)\0)".to_string()),
            "{:?}",
            m.recorded()
        );
        assert_eq!(out["value"], json!(["A", "B", "E"]));
    }

    /// A file stored "A feat. B" while MusicBee showed it as "A & B"; saving
    /// the name the editor showed must not replace what was stored.
    #[test]
    fn saving_the_names_it_was_shown_keeps_the_stored_artist() {
        let m = library();
        m.file_tags
            .lock()
            .unwrap()
            .get_mut("/a.flac")
            .unwrap()
            .extend([(32, "A & B".to_string()), (33, "A feat. B\0".to_string())]);
        run(
            "now_playing_set_tag",
            json!({ "path": "/a.flac", "key": "artist", "value": ["A & B"] }),
            &m,
        )
        .unwrap();
        assert!(
            m.recorded()
                .contains(&"write_now_playing_tag(33,A feat. B\0)".to_string()),
            "{:?}",
            m.recorded()
        );
    }

    #[test]
    fn writing_the_credits_stores_each_role() {
        let m = split_library();
        let out = run(
            "now_playing_set_tag",
            json!({ "path": "/a.flac", "key": "artist_credits", "value": [
                { "name": "A", "role": "artist" },
                { "name": "D", "role": "remixer" },
            ] }),
            &m,
        )
        .unwrap();
        assert!(
            m.recorded()
                .contains(&"write_now_playing_tag(33,A\0\u{4}D\0)".to_string())
        );
        assert_eq!(out["value"][1], json!({ "name": "D", "role": "remixer" }));
    }

    #[test]
    fn a_credit_with_an_unknown_role_writes_nothing() {
        let m = split_library();
        let err = run(
            "now_playing_set_tag",
            json!({ "path": "/a.flac", "key": "artist_credits", "value": [
                { "name": "A", "role": "producer" },
            ] }),
            &m,
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
        assert!(
            !m.recorded()
                .iter()
                .any(|c| c.starts_with("write_now_playing_tag"))
        );
    }

    #[test]
    fn tag_values_counts_artists_and_guests_by_name() {
        let out = run("tag_values", json!({ "key": "artist" }), &split_library()).unwrap();
        assert_eq!(
            out["values"],
            json!([{ "value": "A", "count": 1 }, { "value": "B", "count": 1 }])
        );
    }
}
