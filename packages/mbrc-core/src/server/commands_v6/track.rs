//! V6 track domain: the canonical typed `track` schema.
//!
//! The schema the library, playlist and now-playing lists all share, plus the
//! by-path `track_get` and the content-addressed `cover_get` fetch (#136).
//!
//! The typed fields come from raw MusicBee tag strings (`TrackTags`), parsed here:
//! `year` (4-digit extracted from a possibly-full-date), `duration_ms` (`m:ss`
//! parsed, #112), `rating` (comma-or-dot float, #114). `date_added` is already
//! ISO-8601 (formatted C#-side) and passes through.

use std::time::{Duration, Instant, UNIX_EPOCH};

use serde_json::{Value, json};

use mbrc_wire::v6::ErrorCode;

use super::{OpResult, V6Error, internal, req_str};
use crate::cover::cover_identifier;
use crate::cover::store::{Artwork, CoverStore};
use crate::metadata_cache::{CachedTags, MetadataCache};
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
    let cover_hash = Covers::new(cover_store, p).track(&tags);
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

/// How long one answer may spend reading albumless tracks' artwork before the
/// rest of its tracks go without a `cover_hash`, to be read for a later page.
const READ_BUDGET: Duration = Duration::from_millis(500);

/// Resolves `cover_hash` for the tracks of one answer.
///
/// A track on an album takes the album's cover. A track with no album has a
/// cover of its own (#136), read the first time an answer carries it and kept
/// until its file changes: albumless tracks fold into one album per artist, so
/// that album's cover would answer for every single the artist has. The work is
/// bounded by the tracks a client is shown, never by the library.
pub(crate) struct Covers<'a> {
    store: Option<&'a CoverStore>,
    providers: &'a dyn Providers,
    deadline: Instant,
}

impl<'a> Covers<'a> {
    pub(crate) fn new(store: Option<&'a CoverStore>, providers: &'a dyn Providers) -> Self {
        Self {
            store,
            providers,
            deadline: Instant::now() + READ_BUDGET,
        }
    }

    pub(crate) fn track(&self, tags: &TrackTags) -> Option<String> {
        self.resolve(
            &tags.src,
            album_key_artist(&tags.album_artist, &tags.artist),
            &tags.album,
        )
    }

    pub(crate) fn cached(&self, tags: &CachedTags) -> Option<String> {
        self.resolve(
            &tags.src,
            album_key_artist(&tags.album_artist, &tags.artist),
            &tags.album,
        )
    }

    fn resolve(&self, src: &str, artist: &str, album: &str) -> Option<String> {
        if album.is_empty() {
            self.own(src)
        } else {
            album_cover_hash(self.store, artist, album)
        }
    }

    /// An albumless track's own cover. A file the core cannot see the modified
    /// time of, such as a stream, has none.
    fn own(&self, src: &str) -> Option<String> {
        let store = self.store?;
        let modified = modified_secs(src)?;
        let key = track_artwork_key(src);
        if let Some(cover) = store.item_cover(&key).filter(|c| c.is_current(modified)) {
            return cover.hash;
        }
        if Instant::now() >= self.deadline {
            return None;
        }
        let raw = match crate::server::track_artwork(self.providers, src) {
            Artwork::Found(raw) => Some(raw),
            Artwork::Missing => None,
            Artwork::Unavailable => return None,
        };
        store
            .cache_item_cover(&key, raw.as_deref(), modified)
            .unwrap_or_else(|error| {
                tracing::debug!(src, %error, "track cover: store failed");
                None
            })
    }
}

/// The artist half of the album key: the album artist, else the artist.
fn album_key_artist<'t>(album_artist: &'t str, artist: &'t str) -> &'t str {
    if album_artist.is_empty() {
        artist
    } else {
        album_artist
    }
}

/// A file's modified time in unix seconds, as the cover build records it.
fn modified_secs(path: &str) -> Option<i64> {
    let modified = std::fs::metadata(path).ok()?.modified().ok()?;
    let secs = modified.duration_since(UNIX_EPOCH).ok()?.as_secs();
    i64::try_from(secs).ok()
}

/// The `cover_hash` for the artwork MusicBee says is playing, which is not
/// always the album's.
///
/// A track whose own art differs from the record it is on - a compilation, a
/// podcast episode - gets the wrong picture from an album cache, or none. The
/// bytes announced for the playing track are already cached, so this costs no
/// host call: it stores them once and answers from the store after. Before
/// MusicBee has spoken, the track's usual cover answers.
pub(crate) fn playing_cover_hash(
    covers: &Covers<'_>,
    tags: &TrackTags,
    artwork_b64: &str,
) -> Option<String> {
    let usual = || covers.track(tags);
    let store = match covers.store {
        Some(store) => store,
        None => return usual(),
    };
    if artwork_b64.is_empty() {
        return usual();
    }
    let key = playing_artwork_key(&tags.src);
    if let Some(hash) = store.hash_for(&key) {
        return Some(hash);
    }
    let Some(bytes) = crate::cover::from_base64(artwork_b64) else {
        return usual();
    };
    store.cache_cover(&key, &bytes).ok().or_else(usual)
}

/// The store key an albumless track's own cover is kept under.
fn track_artwork_key(src: &str) -> String {
    format!("track:{src}")
}

/// The key the playing track's announced artwork is kept under, apart from the
/// album keys so that neither can answer for the other.
fn playing_artwork_key(src: &str) -> String {
    format!("playing:{src}")
}

/// Resolve an album's `cover_hash` from its `(artist, album)` key - the shared
/// album-keyed lookup the library domain uses for album items too.
///
/// An artist's tracks with no album are a group, not a record: the picture
/// keyed for it is one single's, so the group has none.
pub(crate) fn album_cover_hash(
    store: Option<&CoverStore>,
    artist: &str,
    album: &str,
) -> Option<String> {
    if album.is_empty() {
        return None;
    }
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

/// The canonical V6 `track` from a cached row.
///
/// The cache stores what the host already parsed, so this reads the same fields
/// without re-parsing a `m:ss` string or a locale-shaped rating. It must emit
/// exactly what [`track_json`] does for the same track: a client cannot tell
/// which one answered it, and `cached_and_live_tracks_agree` fails if they part.
pub(crate) fn cached_track_json(tags: &CachedTags, cover_hash: Option<&str>) -> Value {
    let mut obj = json!({
        "src": tags.src,
        "artist": tags.artist,
        "title": tags.title,
        "album": tags.album,
        "album_artist": tags.album_artist,
        "track_no": tags.track_no,
        "disc_no": tags.disc_no,
        "genre": tags.genre,
        "year": (tags.year > 0).then_some(tags.year as i64),
        "duration_ms": (tags.duration_ms > 0).then_some(tags.duration_ms),
        "rating": (tags.rating > 0.0).then_some(tags.rating as f64),
        "date_added": non_empty(&tags.date_added),
    });
    if let Some(hash) = cover_hash {
        obj["cover_hash"] = json!(hash);
    }
    obj
}

/// The tags for a set of paths, answered from the shared cache and asked of the
/// host only for what the cache does not hold.
///
/// The cache is the one the library browse paths fill, so a playlist or queue of
/// tracks a client has already browsed costs no host call at all. What the host
/// does answer is written back, so the second read of a cold list is warm.
pub(crate) fn tags_for_paths(
    p: &dyn Providers,
    cache: Option<&MetadataCache>,
    paths: &[String],
) -> Result<Vec<CachedTags>, V6Error> {
    let mut hits: Vec<CachedTags> = Vec::new();
    let mut misses: Vec<String> = Vec::new();
    for path in paths {
        match cache.and_then(|c| c.track_tags(path)) {
            Some(cached) => hits.push(cached),
            None => misses.push(path.clone()),
        }
    }
    if misses.is_empty() {
        return Ok(hits);
    }
    let fetched = p.tracks_detailed_for_paths(misses).map_err(internal)?;
    let filled: Vec<CachedTags> = fetched.iter().map(CachedTags::from).collect();
    if let Some(cache) = cache {
        cache.put_track_tags(&filled);
    }
    hits.extend(filled);
    Ok(hits)
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

    /// The two builders answer for the same track, so a client cannot tell
    /// whether the cache or the host filled its page.
    #[test]
    fn cached_and_live_tracks_agree() {
        let live = TrackTags {
            src: "C:/m/a.mp3".into(),
            artist: "Artist".into(),
            title: "Title".into(),
            album: "Album".into(),
            album_artist: "AlbumArtist".into(),
            track_no: 3,
            disc_no: 1,
            genre: "Rock".into(),
            year: "12/03/2007".into(),
            duration: "3:45".into(),
            rating: "3.5".into(),
            date_added: "2024-01-02T03:04:05Z".into(),
        };
        let cached = CachedTags::from(&live);
        assert_eq!(track_json(&live, None), cached_track_json(&cached, None));
        assert_eq!(
            track_json(&live, Some("abc")),
            cached_track_json(&cached, Some("abc"))
        );
    }

    /// An unknown tag is `null` on both sides rather than a zero that reads as
    /// a real value.
    #[test]
    fn cached_and_live_tracks_agree_when_tags_say_nothing() {
        let live = TrackTags {
            src: "C:/m/b.mp3".into(),
            ..Default::default()
        };
        let cached = CachedTags::from(&live);
        let out = cached_track_json(&cached, None);
        assert_eq!(track_json(&live, None), out);
        assert!(out["year"].is_null());
        assert!(out["duration_ms"].is_null());
        assert!(out["rating"].is_null());
        assert!(out["date_added"].is_null());
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
        let out = dispatch(
            "track_get",
            &json!({ "src": r"C:\Music\song.mp3" }),
            &m,
            Some(&store),
        )
        .unwrap()
        .unwrap();
        assert_eq!(out["title"], "Title");
        assert_eq!(out["cover_hash"], hash);
    }

    /// The album cache answers for a record, not for what is playing: a podcast
    /// episode or a compilation track has art of its own and no album entry.
    #[test]
    fn the_playing_track_is_hashed_by_its_own_artwork() {
        let dir = std::env::temp_dir().join("mbrc-v6-playing-cover");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.to_string_lossy().into_owned();
        let store = CoverStore::new(Db::open(&path), path.clone());
        let artwork = crate::cover::to_base64(&test_jpeg_bytes(120, 120));

        let mut tags = tags();
        tags.album = "An Album Nothing Cached".into();
        let m = MockProviders::default();
        let covers = Covers::new(Some(&store), &m);
        let hash = playing_cover_hash(&covers, &tags, &artwork).unwrap();

        assert!(store.read_cover_bytes(&hash).is_some());
        // Answered from the store the second time, so a client polling the
        // playing track does not re-hash the same image on every read.
        assert_eq!(playing_cover_hash(&covers, &tags, &artwork), Some(hash));
    }

    /// Before MusicBee has announced the artwork there is nothing to hash, and
    /// the album it belongs to is a better answer than a blank pane.
    #[test]
    fn without_announced_artwork_the_album_answers() {
        let dir = std::env::temp_dir().join("mbrc-v6-playing-cover-album");
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let path = dir.to_string_lossy().into_owned();
        let store = CoverStore::new(Db::open(&path), path.clone());
        let tags = tags();
        let album = store
            .cache_cover(
                &cover_identifier(&tags.album_artist, &tags.album),
                &test_jpeg_bytes(200, 200),
            )
            .unwrap();

        let m = MockProviders::default();
        let covers = Covers::new(Some(&store), &m);
        assert_eq!(playing_cover_hash(&covers, &tags, ""), Some(album));
    }

    /// A store, and an albumless track whose file exists, under a fresh dir.
    fn albumless(name: &str) -> (CoverStore, TrackTags) {
        let dir = std::env::temp_dir().join(format!("mbrc-v6-albumless-{name}"));
        let _ = std::fs::remove_dir_all(&dir);
        std::fs::create_dir_all(&dir).unwrap();
        let src = dir.join("single.mp3");
        std::fs::write(&src, b"audio").unwrap();
        let store = CoverStore::open_at(&dir);
        let mut tags = tags();
        tags.src = src.to_string_lossy().into_owned();
        tags.album = String::new();
        (store, tags)
    }

    fn artwork_reads(m: &MockProviders) -> usize {
        m.recorded()
            .iter()
            .filter(|call| call.starts_with("artwork_raw"))
            .count()
    }

    #[test]
    fn an_albumless_track_has_its_own_cover_not_its_artists_singles() {
        let (store, tags) = albumless("own");
        let artists_singles = store
            .cache_cover(
                &cover_identifier(&tags.album_artist, ""),
                &test_jpeg_bytes(200, 200),
            )
            .unwrap();
        let m = MockProviders {
            artwork_raw: test_jpeg_bytes(90, 90),
            ..Default::default()
        };

        let own = Covers::new(Some(&store), &m).track(&tags).unwrap();

        assert_ne!(own, artists_singles);
        assert!(store.read_cover_bytes(&own).is_some());
    }

    #[test]
    fn an_albumless_cover_is_read_once_until_its_file_changes() {
        let (store, tags) = albumless("once");
        let m = MockProviders {
            artwork_raw: test_jpeg_bytes(90, 90),
            ..Default::default()
        };
        let first = Covers::new(Some(&store), &m).track(&tags);
        assert_eq!(Covers::new(Some(&store), &m).track(&tags), first);
        assert_eq!(artwork_reads(&m), 1);

        let later = std::time::SystemTime::now() + Duration::from_secs(120);
        std::fs::File::options()
            .write(true)
            .open(&tags.src)
            .unwrap()
            .set_modified(later)
            .unwrap();
        Covers::new(Some(&store), &m).track(&tags);
        assert_eq!(artwork_reads(&m), 2, "a changed file is read again");
    }

    #[test]
    fn an_albumless_track_without_artwork_is_not_asked_again() {
        let (store, tags) = albumless("none");
        let m = MockProviders::default(); // no artwork
        assert_eq!(Covers::new(Some(&store), &m).track(&tags), None);
        assert_eq!(Covers::new(Some(&store), &m).track(&tags), None);
        assert_eq!(artwork_reads(&m), 1);
    }

    #[test]
    fn past_the_read_budget_an_albumless_track_waits_for_a_later_page() {
        let (store, tags) = albumless("budget");
        let m = MockProviders {
            artwork_raw: test_jpeg_bytes(90, 90),
            ..Default::default()
        };
        let spent = Covers {
            store: Some(&store),
            providers: &m,
            deadline: Instant::now(),
        };
        assert_eq!(spent.track(&tags), None);
        assert_eq!(artwork_reads(&m), 0);
        assert!(Covers::new(Some(&store), &m).track(&tags).is_some());
    }

    #[test]
    fn a_stream_with_no_album_has_no_cover_and_costs_no_read() {
        let (store, mut tags) = albumless("stream");
        tags.src = "http://radio.example/stream".into();
        let m = MockProviders {
            artwork_raw: test_jpeg_bytes(90, 90),
            ..Default::default()
        };
        assert_eq!(Covers::new(Some(&store), &m).track(&tags), None);
        assert_eq!(artwork_reads(&m), 0);
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
