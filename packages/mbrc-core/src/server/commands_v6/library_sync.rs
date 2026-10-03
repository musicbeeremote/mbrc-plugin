//! `library_changes`: what moved in the library since a client last read it.
//!
//! For a client that keeps its own copy of the library (#15). It reads the
//! whole library once, keeps the `{epoch, generation}` cursor the read ends
//! with, and from then on asks only for what moved after it. The change log
//! behind it is [`crate::library_changes`].

use serde_json::{Value, json};

use mbrc_wire::v6::ErrorCode;

use super::{OpResult, V6Error, opt_i64, track};
use crate::cover::store::CoverStore;
use crate::library_changes::{Change, ChangePage, Continuation, Cursor, ReadFrom};
use crate::metadata_cache::{CachedTags, MetadataCache};
use crate::providers::Providers;

/// Changes served per page when the request names no `limit`.
const DEFAULT_LIMIT: i64 = 1000;
/// The most changes one page serves.
///
/// Every upsert carries a whole track, and the first read of a library is all
/// upserts, so an unbounded page would be the library in one frame.
const MAX_LIMIT: i64 = 5000;

pub fn changes(
    data: &Value,
    p: &dyn Providers,
    store: Option<&CoverStore>,
    cache: Option<&MetadataCache>,
) -> OpResult {
    let since = cursor_arg(data)?;
    let after = continuation_arg(data)?;
    let limit = opt_i64(data, "limit")?
        .unwrap_or(DEFAULT_LIMIT)
        .clamp(1, MAX_LIMIT) as usize;

    let from = match (&after, &since) {
        (Some(after), _) => ReadFrom::Resume(after),
        (None, Some(since)) => ReadFrom::Since(since),
        (None, None) => ReadFrom::Start,
    };
    let page = cache
        .filter(|c| c.is_validated())
        .and_then(|c| c.changes().read(from, limit));
    let (Some(cache), Some(page)) = (cache, page) else {
        return Err(V6Error::new(
            ErrorCode::Unavailable,
            "the library index is being built; retry on library_changed",
        ));
    };

    Ok(match page {
        ChangePage::Resync { head } => json!({
            "epoch": head.epoch,
            "generation": head.generation,
            "resync": true,
            "total": 0,
            "items": [],
            "next": null,
        }),
        ChangePage::Changes {
            head,
            total,
            changes,
            next,
        } => json!({
            "epoch": head.epoch,
            "generation": head.generation,
            "resync": false,
            "total": total,
            "items": items(&changes, p, store, cache)?,
            "next": next.map(continuation_json),
        }),
    })
}

/// The page's changes as wire items, each upsert carrying the canonical track.
///
/// A path the host no longer describes is left out: it was removed after the
/// pass that filed it, and the next pass files that.
fn items(
    changes: &[Change],
    p: &dyn Providers,
    store: Option<&CoverStore>,
    cache: &MetadataCache,
) -> Result<Vec<Value>, V6Error> {
    let live: Vec<String> = changes
        .iter()
        .filter(|c| c.live)
        .map(|c| c.src.clone())
        .collect();
    let tags = track::tags_for_paths(p, Some(cache), &live)?;
    let by_path: std::collections::HashMap<&str, &CachedTags> =
        tags.iter().map(|t| (t.src.as_str(), t)).collect();
    let covers = track::Covers::new(store, p);

    Ok(changes
        .iter()
        .filter_map(|c| {
            if !c.live {
                return Some(json!({ "change": "delete", "src": c.src }));
            }
            let t = by_path.get(c.src.as_str())?;
            Some(json!({
                "change": "upsert",
                "track": track::cached_track_json(t, covers.cached(t).as_deref()),
            }))
        })
        .collect())
}

/// The optional `since` cursor: `{ epoch, generation }`.
fn cursor_arg(data: &Value) -> Result<Option<Cursor>, V6Error> {
    let Some(since) = object_arg(data, "since")? else {
        return Ok(None);
    };
    Ok(Some(Cursor {
        epoch: field_str(since, "since", "epoch")?,
        generation: field_u64(since, "since", "generation")?,
    }))
}

/// The optional `after` continuation: a previous page's `next`, passed back as is.
fn continuation_arg(data: &Value) -> Result<Option<Continuation>, V6Error> {
    let Some(after) = object_arg(data, "after")? else {
        return Ok(None);
    };
    let since = match after.get("since") {
        None | Some(Value::Null) => None,
        Some(_) => Some(field_u64(after, "after", "since")?),
    };
    Ok(Some(Continuation {
        epoch: field_str(after, "after", "epoch")?,
        since,
        until: field_u64(after, "after", "until")?,
        total: field_u64(after, "after", "total")?,
        generation: field_u64(after, "after", "generation")?,
        src: field_str(after, "after", "src")?,
    }))
}

fn continuation_json(c: Continuation) -> Value {
    json!({
        "epoch": c.epoch,
        "since": c.since,
        "until": c.until,
        "total": c.total,
        "generation": c.generation,
        "src": c.src,
    })
}

fn object_arg<'a>(data: &'a Value, field: &str) -> Result<Option<&'a Value>, V6Error> {
    match data.get(field) {
        None | Some(Value::Null) => Ok(None),
        Some(v @ Value::Object(_)) => Ok(Some(v)),
        Some(_) => Err(V6Error::field(
            ErrorCode::InvalidField,
            field,
            format!("{field} must be an object"),
        )),
    }
}

fn field_str(obj: &Value, parent: &str, field: &str) -> Result<String, V6Error> {
    match obj.get(field) {
        Some(Value::String(s)) => Ok(s.clone()),
        None => Err(V6Error::field(
            ErrorCode::MissingField,
            &format!("{parent}.{field}"),
            format!("missing required field: {parent}.{field}"),
        )),
        Some(_) => Err(V6Error::field(
            ErrorCode::InvalidField,
            &format!("{parent}.{field}"),
            format!("{parent}.{field} must be a string"),
        )),
    }
}

fn field_u64(obj: &Value, parent: &str, field: &str) -> Result<u64, V6Error> {
    match obj.get(field) {
        Some(v) if v.as_u64().is_some() => Ok(v.as_u64().unwrap_or_default()),
        None => Err(V6Error::field(
            ErrorCode::MissingField,
            &format!("{parent}.{field}"),
            format!("missing required field: {parent}.{field}"),
        )),
        Some(_) => Err(V6Error::field(
            ErrorCode::InvalidField,
            &format!("{parent}.{field}"),
            format!("{parent}.{field} must be a non-negative integer"),
        )),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::TrackTags;
    use crate::providers::MockProviders;
    use crate::store::Db;

    fn cache(name: &str) -> MetadataCache {
        let dir = std::env::temp_dir().join(format!("mbrc-library-sync-{name}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let cache = MetadataCache::new(Db::open(dir.to_str().unwrap()));
        cache.reconcile(&[], 1);
        cache
    }

    fn tags(src: &str, title: &str) -> TrackTags {
        TrackTags {
            src: src.into(),
            title: title.into(),
            ..Default::default()
        }
    }

    fn library(paths: &[&str]) -> Vec<String> {
        paths.iter().map(|p| p.to_string()).collect()
    }

    fn call(data: Value, m: &MockProviders, cache: &MetadataCache) -> OpResult {
        changes(&data, m, None, Some(cache))
    }

    fn srcs(out: &Value) -> Vec<(String, String)> {
        out["items"]
            .as_array()
            .unwrap()
            .iter()
            .map(|i| {
                let src = i["src"].as_str().or(i["track"]["src"].as_str()).unwrap();
                (i["change"].as_str().unwrap().to_string(), src.to_string())
            })
            .collect()
    }

    fn mock(all: &[(&str, &str)]) -> MockProviders {
        MockProviders {
            tracks_detailed: all.iter().map(|(s, t)| tags(s, t)).collect(),
            ..Default::default()
        }
    }

    #[test]
    fn a_first_read_is_the_whole_library_as_upserts() {
        let cache = cache("first");
        cache.record_changes(Some(&library(&["/a.mp3", "/b.mp3"])), &[]);
        let m = mock(&[("/a.mp3", "A"), ("/b.mp3", "B")]);

        let out = call(json!({}), &m, &cache).unwrap();
        assert_eq!(out["resync"], false);
        assert_eq!(out["generation"], 1);
        assert_eq!(out["total"], 2);
        assert_eq!(out["next"], Value::Null);
        assert_eq!(
            srcs(&out),
            vec![
                ("upsert".into(), "/a.mp3".into()),
                ("upsert".into(), "/b.mp3".into())
            ]
        );
        assert_eq!(out["items"][0]["track"]["title"], "A");
    }

    #[test]
    fn a_cursor_gets_upserts_and_deletes_after_it() {
        let cache = cache("since");
        cache.record_changes(Some(&library(&["/a.mp3", "/b.mp3"])), &[]);
        let m = mock(&[("/a.mp3", "A"), ("/c.mp3", "C")]);
        let first = call(json!({}), &m, &cache).unwrap();

        cache.record_changes(Some(&library(&["/a.mp3", "/c.mp3"])), &[]);
        let since = json!({ "epoch": first["epoch"], "generation": first["generation"] });
        let out = call(json!({ "since": since }), &m, &cache).unwrap();
        assert_eq!(out["generation"], 2);
        assert_eq!(
            srcs(&out),
            vec![
                ("delete".into(), "/b.mp3".into()),
                ("upsert".into(), "/c.mp3".into())
            ]
        );
    }

    #[test]
    fn next_is_passed_back_as_after_until_it_is_null() {
        let cache = cache("pages");
        let all = ["/a.mp3", "/b.mp3", "/c.mp3"];
        cache.record_changes(Some(&library(&all)), &[]);
        let m = mock(&[("/a.mp3", "A"), ("/b.mp3", "B"), ("/c.mp3", "C")]);

        let mut seen = Vec::new();
        let mut request = json!({ "limit": 2 });
        loop {
            let out = call(request.clone(), &m, &cache).unwrap();
            seen.extend(srcs(&out).into_iter().map(|(_, s)| s));
            if out["next"].is_null() {
                break;
            }
            request = json!({ "limit": 2, "after": out["next"] });
        }
        assert_eq!(seen, library(&all));
    }

    #[test]
    fn a_cursor_from_another_library_is_told_to_resync() {
        let cache = cache("resync");
        cache.record_changes(Some(&library(&["/a.mp3"])), &[]);
        let m = mock(&[("/a.mp3", "A")]);
        let out = call(
            json!({ "since": { "epoch": "not-this-one", "generation": 1 } }),
            &m,
            &cache,
        )
        .unwrap();
        assert_eq!(out["resync"], true);
        assert_eq!(out["items"], json!([]));
    }

    #[test]
    fn a_log_not_yet_started_is_unavailable() {
        let cache = cache("unstarted");
        let err = call(json!({}), &MockProviders::default(), &cache).unwrap_err();
        assert_eq!(err.code, ErrorCode::Unavailable);
    }

    #[test]
    fn a_malformed_cursor_names_the_field() {
        let cache = cache("malformed");
        cache.record_changes(Some(&library(&["/a.mp3"])), &[]);
        let err = call(
            json!({ "since": { "epoch": "e", "generation": -1 } }),
            &MockProviders::default(),
            &cache,
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
        assert_eq!(err.field.as_deref(), Some("since.generation"));

        let err = call(json!({ "since": 3 }), &MockProviders::default(), &cache).unwrap_err();
        assert_eq!(err.field.as_deref(), Some("since"));
    }

    #[test]
    fn a_track_the_host_no_longer_describes_is_left_out() {
        let cache = cache("vanished");
        cache.record_changes(Some(&library(&["/a.mp3", "/gone.mp3"])), &[]);
        let m = mock(&[("/a.mp3", "A")]);
        let out = call(json!({}), &m, &cache).unwrap();
        assert_eq!(srcs(&out), vec![("upsert".into(), "/a.mp3".into())]);
    }
}
