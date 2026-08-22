//! V6 track domain: the canonical typed `track` schema (shared by the library /
//! playlist / now-playing lists that land in later domains), the by-path
//! `track_get`, and the content-addressed `cover_get` fetch (#136).
//!
//! The typed fields come from raw MusicBee tag strings (`TrackTags`), parsed here:
//! `year` (4-digit extracted from a possibly-full-date), `duration_ms` (`m:ss`
//! parsed, #112), `rating` (comma-or-dot float, #114). `date_added` is already
//! ISO-8601 (formatted C#-side) and passes through.

use serde_json::{json, Value};

use mbrc_wire::v6::ErrorCode;

use super::{internal, req_str, OpResult, V6Error};
use crate::cover::cover_identifier;
use crate::cover::store::CoverStore;
use crate::protocol::messages::TrackTags;
use crate::providers::Providers;

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &["track_get", "cover_get"];

/// Dispatch a track/cover op. `None` if `op` is not in this domain.
pub fn dispatch(
    op: &str,
    data: &Value,
    p: &dyn Providers,
    cover_store: Option<&CoverStore>,
) -> Option<OpResult> {
    Some(match op {
        "track_get" => track_get(data, p, cover_store),
        "cover_get" => cover_get(data, cover_store),
        _ => return None,
    })
}

fn track_get(data: &Value, p: &dyn Providers, cover_store: Option<&CoverStore>) -> OpResult {
    let src = req_str(data, "src")?;
    let tags = p
        .tracks_detailed_for_paths(vec![src.to_string()])
        .map_err(internal)?
        .into_iter()
        .next()
        .ok_or_else(|| V6Error::new(ErrorCode::NotFound, format!("no track for src: {src}")))?;
    let cover_hash = cover_hash_for(cover_store, &tags);
    Ok(track_json(&tags, cover_hash.as_deref()))
}

/// Content-addressed cover fetch (#136). Returns the image bytes (base64), a
/// `not_modified` marker when the client already holds this content, or a
/// `not_found` error.
fn cover_get(data: &Value, cover_store: Option<&CoverStore>) -> OpResult {
    let hash = req_str(data, "hash")?;
    let client_hash = data
        .get("client_hash")
        .and_then(Value::as_str)
        .unwrap_or("");
    // etag short-circuit (same rule as the V4 `serve_cover`): the client already
    // has this exact content.
    if !client_hash.is_empty() && client_hash == hash {
        return Ok(json!({ "hash": hash, "not_modified": true }));
    }
    let store =
        cover_store.ok_or_else(|| V6Error::new(ErrorCode::NotFound, "cover store unavailable"))?;
    match store.read_cover_base64(hash) {
        Some(image) => Ok(json!({ "hash": hash, "image": image })),
        None => Err(V6Error::new(
            ErrorCode::NotFound,
            format!("no cover for hash: {hash}"),
        )),
    }
}

/// Resolve a track's `cover_hash` from the album key (`album_artist` when present,
/// else `artist`). The album-keyed store covers every track that belongs to an
/// album; the per-src namespace for albumless singles (#136 §3.2) is a later step.
pub(crate) fn cover_hash_for(store: Option<&CoverStore>, tags: &TrackTags) -> Option<String> {
    let artist = if tags.album_artist.is_empty() {
        &tags.artist
    } else {
        &tags.album_artist
    };
    album_cover_hash(store, artist, &tags.album)
}

/// Resolve an album's `cover_hash` from its `(artist, album)` key - the shared
/// album-keyed lookup the library domain uses for album items too.
pub(crate) fn album_cover_hash(
    store: Option<&CoverStore>,
    artist: &str,
    album: &str,
) -> Option<String> {
    store?.hash_for(&cover_identifier(artist, album))
}

/// Build the canonical V6 `track` from raw tags: base fields always present, the
/// four typed fields `null` when unknown, `cover_hash` omitted when absent.
pub(crate) fn track_json(tags: &TrackTags, cover_hash: Option<&str>) -> Value {
    let mut obj = json!({
        "src": tags.src,
        "artist": tags.artist,
        "title": tags.title,
        "album": tags.album,
        "album_artist": tags.album_artist,
        "track_no": tags.track_no,
        "disc_no": tags.disc_no,
        "genre": tags.genre,
        "year": parse_year(&tags.year),
        "duration_ms": parse_duration_ms(&tags.duration),
        "rating": parse_rating(&tags.rating),
        "date_added": non_empty(&tags.date_added),
    });
    if let Some(hash) = cover_hash {
        obj["cover_hash"] = json!(hash);
    }
    obj
}

fn non_empty(s: &str) -> Option<&str> {
    (!s.is_empty()).then_some(s)
}

/// Extract the 4-digit year from a Year tag that may be a full date
/// (`"12/03/2007"` -> 2007, `"2007"` -> 2007). A 2-digit year yields `None`.
pub(crate) fn parse_year(raw: &str) -> Option<i64> {
    raw.split(|c: char| !c.is_ascii_digit())
        .filter(|t| t.len() == 4)
        .filter_map(|t| t.parse::<i64>().ok())
        .find(|y| (1000..=9999).contains(y))
}

/// `"m:ss"` / `"h:mm:ss"` / `"ss"` -> milliseconds (#112: only a formatted string
/// is available per path).
pub(crate) fn parse_duration_ms(raw: &str) -> Option<i64> {
    let raw = raw.trim();
    if raw.is_empty() {
        return None;
    }
    let mut total_secs: i64 = 0;
    for part in raw.split(':') {
        let n: i64 = part.trim().parse().ok()?;
        if n < 0 {
            return None;
        }
        total_secs = total_secs.checked_mul(60)?.checked_add(n)?;
    }
    total_secs.checked_mul(1000)
}

/// `"3.5"` / `"3,5"` / `"0"` / `""` -> a 0-5 float, or `None` when unrated
/// (0 or empty). Handles the European comma decimal.
pub(crate) fn parse_rating(raw: &str) -> Option<f64> {
    let raw = raw.trim();
    if raw.is_empty() {
        return None;
    }
    let value: f64 = raw.replace(',', ".").parse().ok()?;
    if value <= 0.0 {
        return None; // 0 = unrated
    }
    Some(value.clamp(0.0, 5.0))
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::cover::test_jpeg_bytes;
    use crate::providers::MockProviders;
    use crate::store::Db;

    fn tags() -> TrackTags {
        TrackTags {
            src: "C:\\Music\\song.mp3".into(),
            artist: "Artist".into(),
            title: "Title".into(),
            album: "Album".into(),
            album_artist: "Album Artist".into(),
            track_no: 4,
            disc_no: 1,
            genre: "Rock".into(),
            year: "12/03/2007".into(),
            duration: "4:17".into(),
            rating: "3,5".into(),
            date_added: "2025-07-09T01:27:00Z".into(),
        }
    }

    #[test]
    fn parsers_cover_the_documented_cases() {
        assert_eq!(parse_year("12/03/2007"), Some(2007));
        assert_eq!(parse_year("2007"), Some(2007));
        assert_eq!(parse_year("07"), None);
        assert_eq!(parse_year(""), None);

        assert_eq!(parse_duration_ms("4:17"), Some(257_000));
        assert_eq!(parse_duration_ms("1:02:03"), Some(3_723_000));
        assert_eq!(parse_duration_ms("45"), Some(45_000));
        assert_eq!(parse_duration_ms(""), None);
        assert_eq!(parse_duration_ms("nope"), None);

        assert_eq!(parse_rating("3,5"), Some(3.5));
        assert_eq!(parse_rating("3.5"), Some(3.5));
        assert_eq!(parse_rating("0"), None);
        assert_eq!(parse_rating(""), None);
        assert_eq!(parse_rating("6"), Some(5.0)); // clamped
    }

    #[test]
    fn track_json_is_typed_and_omits_cover_hash_when_absent() {
        let v = track_json(&tags(), None);
        assert_eq!(v["src"], "C:\\Music\\song.mp3");
        assert_eq!(v["track_no"], 4);
        assert_eq!(v["year"], 2007);
        assert_eq!(v["duration_ms"], 257_000);
        assert_eq!(v["rating"], 3.5);
        assert_eq!(v["date_added"], "2025-07-09T01:27:00Z");
        assert!(v.get("cover_hash").is_none());
    }

    #[test]
    fn track_json_nulls_unknown_typed_fields() {
        let mut t = tags();
        t.year = String::new();
        t.duration = String::new();
        t.rating = "0".into();
        t.date_added = String::new();
        let v = track_json(&t, Some("abc123"));
        assert!(v["year"].is_null());
        assert!(v["duration_ms"].is_null());
        assert!(v["rating"].is_null());
        assert!(v["date_added"].is_null());
        assert_eq!(v["cover_hash"], "abc123");
    }

    #[test]
    fn track_get_reads_tags_and_resolves_cover_hash() {
        let dir = std::env::temp_dir().join("mbrc-v6-track-get");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.to_string_lossy().into_owned();
        let store = CoverStore::new(Db::open(&path), path.clone());
        // Seed the album cover under the same key track_get will resolve.
        let key = cover_identifier("Album Artist", "Album");
        let hash = store.cache_cover(&key, &test_jpeg_bytes(200, 200)).unwrap();

        let m = MockProviders {
            tracks_detailed: vec![tags()],
            ..Default::default()
        };
        let out = dispatch("track_get", &json!({ "src": "x" }), &m, Some(&store))
            .unwrap()
            .unwrap();
        assert_eq!(out["title"], "Title");
        assert_eq!(out["cover_hash"], hash);
    }

    #[test]
    fn track_get_unknown_src_is_not_found() {
        let m = MockProviders::default(); // empty tracks_detailed
        let err = dispatch("track_get", &json!({ "src": "x" }), &m, None)
            .unwrap()
            .unwrap_err();
        assert_eq!(err.code, ErrorCode::NotFound);
    }

    #[test]
    fn cover_get_hit_not_modified_and_miss() {
        let dir = std::env::temp_dir().join("mbrc-v6-cover-get");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.to_string_lossy().into_owned();
        let store = CoverStore::new(Db::open(&path), path.clone());
        let hash = store
            .cache_cover(&cover_identifier("A", "B"), &test_jpeg_bytes(200, 200))
            .unwrap();
        let m = MockProviders::default();

        // Hit -> image.
        let out = dispatch("cover_get", &json!({ "hash": hash }), &m, Some(&store))
            .unwrap()
            .unwrap();
        assert_eq!(out["hash"], hash);
        assert!(out["image"].as_str().unwrap().len() > 10);

        // client_hash == hash -> not_modified (no image).
        let nm = dispatch(
            "cover_get",
            &json!({ "hash": hash, "client_hash": hash }),
            &m,
            Some(&store),
        )
        .unwrap()
        .unwrap();
        assert_eq!(nm["not_modified"], true);
        assert!(nm.get("image").is_none());

        // Unknown hash -> not_found.
        let err = dispatch(
            "cover_get",
            &json!({ "hash": "deadbeef" }),
            &m,
            Some(&store),
        )
        .unwrap()
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::NotFound);
    }

    #[test]
    fn unknown_op_is_not_in_this_domain() {
        let m = MockProviders::default();
        assert!(dispatch("player_status", &json!({}), &m, None).is_none());
    }
}
