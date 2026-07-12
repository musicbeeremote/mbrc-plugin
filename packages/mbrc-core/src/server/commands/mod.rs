//! Command dispatch: maps a wire context to its handler. Handlers are near-pure
//! functions of `(data, &impl Providers) -> HandlerResult`; the connection layer
//! frames the replies and writes them.
//!
//! Adding a command is a 3-line change: a `CommandType`/`QueryType` slot, a
//! handler fn, and one arm here (plus its name in `DISPATCHED_CONTEXTS`, which
//! the `handler_audit` test checks against the maintained V4 surface).

pub mod library;
pub mod nowplaying_list;
pub mod player;
pub mod playlists;
pub mod system;
pub mod track;

use serde_json::Value;

use crate::cover::store::CoverStore;
use crate::metadata_cache::MetadataCache;
use crate::nowplaying::NowPlayingCache;
use crate::protocol::messages::{
    Cover, LastfmStatus, Lyrics, PlayerState, TrackDetails, TrackInfo,
};
use crate::protocol::version::ProtocolVersion;
use crate::providers::Providers;
use crate::wire::WireCodec;

pub use crate::protocol::Platform;

/// Per-dispatch context handed to handlers: the provider boundary, the
/// negotiated protocol version (which selects the wire formatter), the client
/// platform, and (in production) the now-playing cache. Handlers format through
/// `ctx.wire()` and never name a version, so adding V6 is purely a new formatter.
///
/// Now-playing reads go through the `now_*` helpers: when the cache is present
/// they serve from it (no FFI), otherwise they fall back to the provider - so
/// handler unit tests keep exercising the provider path with a bare `Ctx::new`.
pub struct Ctx<'a> {
    pub providers: &'a dyn Providers,
    pub version: ProtocolVersion,
    pub platform: Platform,
    pub now_playing: Option<&'a NowPlayingCache>,
    /// The album cover cache, when wired (production). Absent in handler unit
    /// tests, where the cover handlers fall back to the provider methods.
    pub cover_store: Option<&'a CoverStore>,
    /// The library metadata cache, when wired (production). Absent in handler
    /// unit tests, where the browse/nav handlers go straight to the provider.
    pub metadata_cache: Option<&'a MetadataCache>,
}

impl<'a> Ctx<'a> {
    pub fn new(providers: &'a dyn Providers, version: ProtocolVersion) -> Self {
        Self {
            providers,
            version,
            platform: Platform::Unknown,
            now_playing: None,
            cover_store: None,
            metadata_cache: None,
        }
    }

    /// Set the client platform (from the handshake). Builder-style so existing
    /// two-arg `Ctx::new` call sites (tests) are unaffected.
    pub fn with_platform(mut self, platform: Platform) -> Self {
        self.platform = platform;
        self
    }

    /// Attach the now-playing cache (production wiring). Builder-style.
    pub fn with_now_playing(mut self, cache: &'a NowPlayingCache) -> Self {
        self.now_playing = Some(cache);
        self
    }

    /// Attach the album cover cache (production wiring). Builder-style.
    pub fn with_cover_store(mut self, store: &'a CoverStore) -> Self {
        self.cover_store = Some(store);
        self
    }

    /// Attach the library metadata cache (production wiring). Builder-style.
    pub fn with_metadata_cache(mut self, cache: &'a MetadataCache) -> Self {
        self.metadata_cache = Some(cache);
        self
    }

    /// The wire codec for the negotiated protocol version.
    pub fn wire(&self) -> &'static dyn WireCodec {
        self.version.codec()
    }

    // ── Cached now-playing reads (fall back to the provider when uncached) ──
    pub fn now_track_info(&self) -> Result<TrackInfo, String> {
        match self.now_playing {
            Some(c) => Ok(c.track_info()),
            None => self.providers.track_info(),
        }
    }
    pub fn now_track_details(&self) -> Result<TrackDetails, String> {
        match self.now_playing {
            Some(c) => Ok(c.track_details()),
            None => self.providers.track_details(),
        }
    }
    pub fn now_cover(&self) -> Result<Cover, String> {
        match self.now_playing {
            Some(c) => Ok(c.cover()),
            None => self.providers.cover(),
        }
    }
    pub fn now_lyrics(&self) -> Result<Lyrics, String> {
        match self.now_playing {
            Some(c) => Ok(c.lyrics()),
            None => self.providers.lyrics(),
        }
    }
    pub fn now_rating(&self) -> Result<String, String> {
        match self.now_playing {
            Some(c) => Ok(c.rating()),
            None => self.providers.rating(),
        }
    }
    pub fn now_lfm(&self) -> Result<LastfmStatus, String> {
        match self.now_playing {
            Some(c) => Ok(c.lfm()),
            None => self.providers.lfm_rating(),
        }
    }
    pub fn now_player(&self) -> Result<PlayerState, String> {
        match self.now_playing {
            Some(c) => Ok(c.player()),
            None => self.providers.player_state(),
        }
    }
}

/// A single reply frame: `(context, data)`. The caller applies CRLF framing.
pub type Reply = (String, Value);

/// A handler's result: reply frames, or an error (logged; no reply sent).
pub type HandlerResult = Result<Vec<Reply>, String>;

/// Extract `{offset, limit}` from a paginated request payload (0 when absent).
pub fn pagination(data: &Value) -> (i32, i32) {
    let field = |key: &str| data.get(key).and_then(Value::as_i64).unwrap_or(0) as i32;
    (field("offset"), field("limit"))
}

// ── Lenient value coercion (C# parity) ──
//
// The shipped C# plugin read command payloads with `JToken.ToObject<T>()`, which
// coerces across JSON types: a bare number `5` deserializes to the string "5", a
// numeric string "50" to the int 50, and "true"/1 to a bool. The V4 clients rely
// on this - iOS, for one, sends the now-playing rating as a bare number. The Rust
// rewrite's exact-type accessors (`as_str`/`as_i64`/`as_bool`) reject the other
// representation, silently dropping the set. These helpers restore the coercion
// at the handful of settable-value handlers so V4 stays byte-for-byte compatible.

/// A settable string: a JSON string, or a number rendered to its text form
/// (C# `GetDataOrDefault<string>()` coerced `5` -> "5"). `null`/absent -> `None`,
/// which the handlers treat as a query rather than a set.
pub fn as_set_string(data: &Value) -> Option<String> {
    match data {
        Value::String(s) => Some(s.clone()),
        Value::Number(n) => Some(n.to_string()),
        _ => None,
    }
}

/// An integer from a JSON number or a numeric string (C# `TryGetData<int>()`
/// coerced "50" -> 50). Non-numeric input -> `None`.
pub fn as_int_lenient(data: &Value) -> Option<i64> {
    data.as_i64()
        .or_else(|| data.as_str().and_then(|s| s.trim().parse::<i64>().ok()))
}

/// A bool from a JSON bool, `"true"`/`"false"` (case-insensitive), or `1`/`0`
/// (C# `TryGetData<bool>()` coerced all three). Anything else -> `None`.
pub fn as_bool_lenient(data: &Value) -> Option<bool> {
    match data {
        Value::Bool(b) => Some(*b),
        Value::Number(n) => n.as_i64().map(|i| i != 0),
        Value::String(s) => match s.trim().to_ascii_lowercase().as_str() {
            "true" | "1" => Some(true),
            "false" | "0" => Some(false),
            _ => None,
        },
        _ => None,
    }
}

/// Serialize a DTO as the reply on `context`.
pub fn reply_dto<T: serde::Serialize>(context: &str, dto: &T) -> HandlerResult {
    let data = serde_json::to_value(dto).map_err(|e| e.to_string())?;
    Ok(vec![(context.to_string(), data)])
}

/// Every command context the core dispatches. `handler_audit` checks this
/// against the maintained V4 surface so coverage can't silently regress.
pub const DISPATCHED_CONTEXTS: &[&str] = &[
    "playerplay",
    "playerpause",
    "playerplaypause",
    "playerstop",
    "playernext",
    "playerprevious",
    "playervolume",
    "playermute",
    "playershuffle",
    "playerrepeat",
    "scrobbler",
    "playerstatus",
    "playeroutput",
    "playeroutputswitch",
    // track
    "nowplayingtrack",
    "nowplayingdetails",
    "nowplayingposition",
    "nowplayingcover",
    "nowplayinglyrics",
    "nowplayingrating",
    "nowplayinglfmrating",
    "nowplayingtagchange",
    // now-playing list
    "nowplayinglist",
    "nowplayinglistplay",
    "nowplayinglistremove",
    "nowplayinglistmove",
    "nowplayinglistsearch",
    "nowplayingqueue",
    // library
    "browsegenres",
    "browseartists",
    "browsealbums",
    "browsetracks",
    "librarygenreartists",
    "libraryartistalbums",
    "libraryalbumtracks",
    "libraryalbumcover",
    "librarycovercachebuildstatus",
    "radiostations",
    "libraryplayall",
    // playlists
    "playlistlist",
    "playlistplay",
    // system
    "pluginversion",
    "init",
];

/// Dispatch a command to its handler. Returns `None` if no handler is
/// registered for `context` (so the caller can tell "unknown" from "handled,
/// no reply").
pub fn dispatch(ctx: &Ctx, context: &str, data: &Value) -> Option<HandlerResult> {
    let p = ctx.providers;
    let result = match context {
        // Player handlers take the full ctx (they format through ctx.wire()).
        "playerplay" => player::play(ctx),
        "playerpause" => player::pause(ctx),
        "playerplaypause" => player::play_pause(ctx),
        "playerstop" => player::stop(ctx),
        "playernext" => player::next(ctx),
        "playerprevious" => player::previous(ctx),
        "playervolume" => player::volume(data, ctx),
        "playermute" => player::mute(data, ctx),
        "playershuffle" => player::shuffle(data, ctx),
        "playerrepeat" => player::repeat(data, ctx),
        "scrobbler" => player::scrobble(data, ctx),
        "playerstatus" => player::status(ctx),
        "playeroutput" => player::output(data, ctx),
        "playeroutputswitch" => player::output_switch(data, ctx),
        "nowplayingtrack" => track::track_info(ctx),
        "nowplayingdetails" => track::details(ctx),
        "nowplayingposition" => track::position(data, p),
        "nowplayingcover" => track::cover(ctx),
        "nowplayinglyrics" => track::lyrics(ctx),
        "nowplayingrating" => track::rating(data, ctx),
        "nowplayinglfmrating" => track::lfm_rating(data, ctx),
        "nowplayingtagchange" => track::tag_change(data, p),
        "nowplayinglist" => nowplaying_list::list(data, ctx),
        "nowplayinglistplay" => nowplaying_list::play(data, ctx),
        "nowplayinglistremove" => nowplaying_list::remove(data, p),
        "nowplayinglistmove" => nowplaying_list::move_track(data, p),
        "nowplayinglistsearch" => nowplaying_list::search(data, p),
        "nowplayingqueue" => nowplaying_list::queue(data, ctx),
        "browsegenres" => library::browse_genres(data, ctx),
        "browseartists" => library::browse_artists(data, ctx),
        "browsealbums" => library::browse_albums(data, ctx),
        "browsetracks" => library::browse_tracks(data, ctx),
        "librarygenreartists" => library::genre_artists(data, ctx),
        "libraryartistalbums" => library::artist_albums(data, ctx),
        "libraryalbumtracks" => library::album_tracks(data, ctx),
        "libraryalbumcover" => library::album_cover(data, ctx),
        "librarycovercachebuildstatus" => library::cover_cache_status(ctx),
        "radiostations" => library::radio_stations(data, p),
        "libraryplayall" => library::play_all(data, p),
        "playlistlist" => playlists::list(data, p),
        "playlistplay" => playlists::play(data, p),
        "pluginversion" => system::plugin_version(p),
        "init" => system::init(ctx),
        _ => return None,
    };
    Some(result)
}

#[cfg(test)]
mod audit {
    use super::*;
    use serde_json::json;

    /// Every context we claim to dispatch must actually have an arm (return
    /// `Some`), so `DISPATCHED_CONTEXTS` can't drift from the match.
    #[test]
    fn every_declared_context_has_a_handler() {
        let providers = crate::providers::NullProviders;
        let ctx = Ctx::new(&providers, ProtocolVersion::V4);
        for context in DISPATCHED_CONTEXTS {
            assert!(
                dispatch(&ctx, context, &Value::Null).is_some(),
                "no dispatch arm for declared context {context}"
            );
        }
    }

    // ── Lenient coercion (C# `ToObject<T>()` parity) ──

    #[test]
    fn as_set_string_coerces_number_but_not_null() {
        assert_eq!(as_set_string(&json!("5")).as_deref(), Some("5"));
        assert_eq!(as_set_string(&json!(5)).as_deref(), Some("5")); // iOS bare number
        assert_eq!(as_set_string(&json!("1989")).as_deref(), Some("1989"));
        assert_eq!(as_set_string(&json!(1989)).as_deref(), Some("1989"));
        assert_eq!(as_set_string(&Value::Null), None); // stays a query
    }

    #[test]
    fn as_int_lenient_takes_number_or_numeric_string() {
        assert_eq!(as_int_lenient(&json!(50)), Some(50));
        assert_eq!(as_int_lenient(&json!("50")), Some(50));
        assert_eq!(as_int_lenient(&json!(" 50 ")), Some(50));
        assert_eq!(as_int_lenient(&json!("status")), None); // iOS position poll
        assert_eq!(as_int_lenient(&Value::Null), None);
    }

    #[test]
    fn as_bool_lenient_takes_bool_string_or_number() {
        assert_eq!(as_bool_lenient(&json!(true)), Some(true));
        assert_eq!(as_bool_lenient(&json!("true")), Some(true));
        assert_eq!(as_bool_lenient(&json!("False")), Some(false));
        assert_eq!(as_bool_lenient(&json!(1)), Some(true));
        assert_eq!(as_bool_lenient(&json!(0)), Some(false));
        assert_eq!(as_bool_lenient(&json!("toggle")), None); // handlers treat separately
    }
}
