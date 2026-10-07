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
//! of every track's fields, kept until the tags change.

use std::sync::Arc;
use std::time::Instant;

use serde_json::{Map, Value, json};

use mbrc_wire::v6::ErrorCode;

use super::{OpResult, V6Error, internal, req_str};
use crate::metadata_cache::{MetadataCache, TagColumns};
use crate::multi_value;
use crate::providers::Providers;
use crate::tag_fields::{self, FIELDS, Multi, TagField};

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &[
    "tag_fields",
    "tag_values",
    "now_playing_tags",
    "now_playing_set_tag",
];

/// Paths read per host call while reading every track's fields.
const COLUMN_BATCH: usize = 500;

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

fn fields(p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let names = p
        .field_names(FIELDS.iter().map(|f| f.field).collect())
        .map_err(internal)?;
    let columns = columns(p, cache)?;
    let fields: Vec<Value> = FIELDS
        .iter()
        .zip(names)
        .map(
            |(f, name)| json!({ "key": f.key, "name": name.trim(), "multi_value": is_multi(f, &columns) }),
        )
        .collect();
    Ok(json!({ "fields": fields }))
}

fn values(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let field = field_for(data, "key")?;
    let columns = columns(p, cache)?;
    let column = columns
        .get(&field.field)
        .map(Vec::as_slice)
        .unwrap_or_default();
    let split = is_multi(field, &columns);
    let values: Vec<Value> = multi_value::distinct(column.iter().map(String::as_str), split)
        .into_iter()
        .map(|(value, count)| json!({ "value": value, "count": count }))
        .collect();
    Ok(json!({ "key": field.key, "values": values }))
}

fn now_playing_tags(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let wanted: Vec<&TagField> = match data.get("keys") {
        None | Some(Value::Null) => FIELDS.iter().collect(),
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
    let read = p
        .now_playing_tags(wanted.iter().map(|f| f.field).collect())
        .map_err(internal)?;
    if read.path.is_empty() {
        return Ok(json!({ "path": null, "tags": {} }));
    }
    let columns = if wanted.iter().any(|f| f.multi == Multi::Inferred) {
        columns(p, cache)?
    } else {
        Arc::default()
    };
    let mut tags = Map::new();
    for (field, raw) in wanted.iter().zip(read.values) {
        tags.insert(field.key.into(), shaped(&raw, is_multi(field, &columns)));
    }
    Ok(json!({ "path": read.path, "tags": tags }))
}

fn set_tag(data: &Value, p: &dyn Providers, cache: Option<&MetadataCache>) -> OpResult {
    let path = req_str(data, "path")?;
    let field = field_for(data, "key")?;
    let (stored, as_list) = stored_value(field, data.get("value"))?;

    let result = p
        .write_now_playing_tag(path.to_owned(), field.field, stored.clone())
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
    if let Some(c) = cache {
        c.forget_derived_tags();
    }
    if !stored.trim().is_empty() && result.value.trim().is_empty() {
        return Err(V6Error::new(
            ErrorCode::Unavailable,
            format!(
                "MusicBee accepted {} but kept no value; a custom field holds one only once it is set up in MusicBee's tag preferences",
                field.key
            ),
        ));
    }
    Ok(json!({ "path": path, "key": field.key, "value": shaped(&result.value, as_list) }))
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
        (None, _) => Err(V6Error::field(
            ErrorCode::MissingField,
            "value",
            "value is required; send \"\" or [] to clear the field",
        )),
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

/// A raw value as the wire carries it: split into an array for a multi-value
/// field, as stored otherwise.
fn shaped(raw: &str, multi: bool) -> Value {
    if multi {
        json!(multi_value::values(raw).collect::<Vec<&str>>())
    } else {
        json!(raw)
    }
}

fn is_multi(field: &TagField, columns: &TagColumns) -> bool {
    match field.multi {
        Multi::Never => false,
        Multi::Always => true,
        Multi::Inferred => columns
            .get(&field.field)
            .is_some_and(|column| tag_fields::holds_separated(column)),
    }
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

/// Every track's tag-editing fields, from the cache when kept, otherwise read
/// from the host and kept unless a write overtook the read.
fn columns(p: &dyn Providers, cache: Option<&MetadataCache>) -> Result<Arc<TagColumns>, V6Error> {
    if let Some(kept) = cache.and_then(MetadataCache::tag_columns) {
        return Ok(kept);
    }
    let started = Instant::now();
    let generation = cache.map(MetadataCache::tags_generation);
    let paths = match cache {
        Some(c) if c.track_count() > 0 => {
            c.track_page_paths(0, i32::try_from(c.track_count()).unwrap_or(i32::MAX))
        }
        _ => p.track_paths().map_err(internal)?,
    };
    let ids: Vec<i32> = FIELDS.iter().map(|f| f.field).collect();
    let mut columns: TagColumns = ids
        .iter()
        .map(|id| (*id, Vec::with_capacity(paths.len())))
        .collect();
    for batch in paths.chunks(COLUMN_BATCH) {
        for row in p
            .tags_for_paths(ids.clone(), batch.to_vec())
            .map_err(internal)?
        {
            for (id, value) in ids.iter().zip(row.values) {
                if let Some(column) = columns.get_mut(id) {
                    column.push(value);
                }
            }
        }
    }
    tracing::info!(
        tracks = paths.len(),
        ms = started.elapsed().as_millis() as u64,
        "tag fields read for every track"
    );
    Ok(match (cache, generation) {
        (Some(c), Some(g)) => c.keep_tag_columns(g, columns),
        _ => Arc::new(columns),
    })
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
        assert_eq!(out["fields"].as_array().unwrap().len(), FIELDS.len());
        assert_eq!(field(&out, "custom1")["name"], "Energy");
        assert_eq!(field(&out, "custom1")["multi_value"], false);
        assert_eq!(field(&out, "custom2")["name"], "Instruments");
        assert_eq!(field(&out, "custom2")["multi_value"], true);
        assert_eq!(field(&out, "genre")["multi_value"], true);
        assert_eq!(field(&out, "title")["multi_value"], false);
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
                json!({ "keys": ["genre", "lyrics"] }),
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
}
