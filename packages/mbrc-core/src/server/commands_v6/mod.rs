//! V6 op dispatch.
//!
//! A lean parallel to the legacy [`commands`](super::commands) module: handlers
//! take `(&Value, &dyn Providers, ...)` and return typed V6 response `data` or a
//! typed error, with no `Ctx`/`WireCodec` (those carry V4 spellings). The
//! [`V6Session`](super::session_v6::V6Session) frames the envelope around the
//! returned value.

pub mod library;
pub mod nowplaying;
pub mod nowplaying_list;
pub mod player;
pub mod playlist;
pub mod system;
pub mod track;

use serde_json::{Value, json};

use mbrc_wire::v6::ErrorCode;

use crate::cover::store::CoverStore;
use crate::metadata_cache::MetadataCache;
use crate::nowplaying::NowPlayingCache;
use crate::providers::Providers;

/// Ops the session answers itself, advertised in the handshake capabilities
/// alongside every domain's own list.
const SESSION_OPS: &[&str] = &["handshake", "ping"];

/// Event names the server may emit (best-effort). Advertised in capabilities so a
/// client knows what to expect; it stays in sync with `notifications_v6::build`.
pub const SUPPORTED_EVENTS: &[&str] = &[
    "play_state_changed",
    "volume_changed",
    "mute_changed",
    "now_playing_changed",
    "now_playing_lyrics_changed",
    "now_playing_list_changed",
    "cover_cache_changed",
    "library_changed",
    "server_shutdown",
];

/// The capability set advertised in the handshake response.
///
/// The ops the server accepts and the events it may emit. Additive (#118 §9
/// Q5), so clients tolerate its absence; each domain appends its own `OPS`.
pub fn capabilities() -> Value {
    let ops: Vec<&str> = SESSION_OPS
        .iter()
        .chain(player::OPS)
        .chain(system::OPS)
        .chain(track::OPS)
        .chain(library::OPS)
        .chain(playlist::OPS)
        .chain(nowplaying::OPS)
        .chain(nowplaying_list::OPS)
        .copied()
        .collect();
    json!({ "ops": ops, "events": SUPPORTED_EVENTS })
}

/// A typed V6 op failure: an error code plus a human message. Rendered by the
/// session as `{kind:"response", error:{code, message}}`.
#[derive(Debug)]
pub struct V6Error {
    pub code: ErrorCode,
    pub message: String,
    /// The `data` field at fault, when the error is about one. Reaches the client
    /// as `error.field`, so validation does not have to be read out of prose.
    pub field: Option<String>,
}

impl V6Error {
    pub fn new(code: ErrorCode, message: impl Into<String>) -> Self {
        Self {
            code,
            message: message.into(),
            field: None,
        }
    }

    /// A failure caused by one named `data` field.
    pub fn field(code: ErrorCode, field: &str, message: impl Into<String>) -> Self {
        Self {
            code,
            message: message.into(),
            field: Some(field.to_owned()),
        }
    }
}

/// The result of a V6 op handler: the response `data`, or a typed error.
pub type OpResult = Result<Value, V6Error>;

/// Dispatch a V6 op to its domain handler. `None` = unknown op (the session
/// replies `unknown_op`). New domains chain here with `.or_else(...)`.
pub fn dispatch(
    op: &str,
    data: &Value,
    providers: &dyn Providers,
    now_playing: Option<&NowPlayingCache>,
    cover_store: Option<&CoverStore>,
    metadata_cache: Option<&MetadataCache>,
) -> Option<OpResult> {
    player::dispatch(op, data, providers, now_playing)
        .or_else(|| system::dispatch(op, data, providers))
        .or_else(|| track::dispatch(op, data, providers, cover_store))
        .or_else(|| library::dispatch(op, data, providers, cover_store, metadata_cache))
        .or_else(|| playlist::dispatch(op, data, providers))
        .or_else(|| nowplaying::dispatch(op, data, providers, now_playing, cover_store))
        .or_else(|| nowplaying_list::dispatch(op, data, providers, now_playing, cover_store))
}

/// The shared V6 pagination envelope: `{ total, offset, items }` (#118 §8). `total`
/// is the full count; `items` is the served page (its length conveys the limit).
pub(crate) fn page_json(total: usize, offset: i64, items: Vec<Value>) -> Value {
    json!({ "total": total, "offset": offset, "items": items })
}

/// The page size served when a request names none.
///
/// Absent `limit` used to mean "to the end", which made the laziest possible
/// call - `library_tracks {}` - read every tag in the library and serialize it
/// into one frame. A default page keeps the obvious request cheap; a client that
/// genuinely wants everything still asks for it with an explicit `limit: 0`.
pub(crate) const DEFAULT_PAGE_LIMIT: i64 = 1000;

/// `(offset, limit)` from a paginated request, each clamped to the non-negative
/// `i32` range the providers accept.
///
/// An absent `limit` is [`DEFAULT_PAGE_LIMIT`]; an explicit `0` still means "to
/// the end". Clamping here means the downstream `as i32` casts can never wrap a
/// huge or negative client value into a bogus page index (or an FFI-crossing
/// negative), and the `offset` echoed in the response envelope always matches
/// the one actually used.
pub(crate) fn page_args(data: &Value) -> Result<(i64, i64), V6Error> {
    let offset = opt_i64(data, "offset")?
        .unwrap_or(0)
        .clamp(0, i32::MAX as i64);
    let limit = opt_i64(data, "limit")?
        .unwrap_or(DEFAULT_PAGE_LIMIT)
        .clamp(0, i32::MAX as i64);
    Ok((offset, limit))
}

/// Narrow a client-supplied `i64` index/position to `i32` (the FFI/provider width)
/// without wrapping: an out-of-range value saturates to the `i32` bound, where the
/// MusicBee API treats it as a harmless out-of-range no-op rather than a wrapped
/// (possibly negative) index.
pub(crate) fn i32_saturating(v: i64) -> i32 {
    v.clamp(i32::MIN as i64, i32::MAX as i64) as i32
}

// ── shared field extractors (typed, with the right error code) ──────────────

/// A required integer field (`missing_field` if absent, `invalid_field` if not an int).
pub(crate) fn req_i64(data: &Value, field: &str) -> Result<i64, V6Error> {
    match data.get(field) {
        None => Err(V6Error::field(
            ErrorCode::MissingField,
            field,
            format!("missing required field: {field}"),
        )),
        Some(v) => v.as_i64().ok_or_else(|| {
            V6Error::field(
                ErrorCode::InvalidField,
                field,
                format!("{field} must be an integer"),
            )
        }),
    }
}

/// A required boolean field.
pub(crate) fn req_bool(data: &Value, field: &str) -> Result<bool, V6Error> {
    match data.get(field) {
        None => Err(V6Error::field(
            ErrorCode::MissingField,
            field,
            format!("missing required field: {field}"),
        )),
        Some(v) => v.as_bool().ok_or_else(|| {
            V6Error::field(
                ErrorCode::InvalidField,
                field,
                format!("{field} must be a boolean"),
            )
        }),
    }
}

/// A required string field.
pub(crate) fn req_str<'a>(data: &'a Value, field: &str) -> Result<&'a str, V6Error> {
    match data.get(field) {
        None => Err(V6Error::field(
            ErrorCode::MissingField,
            field,
            format!("missing required field: {field}"),
        )),
        Some(v) => v.as_str().ok_or_else(|| {
            V6Error::field(
                ErrorCode::InvalidField,
                field,
                format!("{field} must be a string"),
            )
        }),
    }
}

/// A required array-of-strings field (`missing_field` if absent, `invalid_field`
/// if not an array of strings).
pub(crate) fn req_str_array(data: &Value, field: &str) -> Result<Vec<String>, V6Error> {
    match data.get(field) {
        None => Err(V6Error::field(
            ErrorCode::MissingField,
            field,
            format!("missing required field: {field}"),
        )),
        Some(Value::Array(arr)) => arr
            .iter()
            .map(|v| {
                v.as_str().map(String::from).ok_or_else(|| {
                    V6Error::field(
                        ErrorCode::InvalidField,
                        field,
                        format!("{field} must be an array of strings"),
                    )
                })
            })
            .collect(),
        Some(_) => Err(V6Error::field(
            ErrorCode::InvalidField,
            field,
            format!("{field} must be an array"),
        )),
    }
}

/// Map a provider/FFI failure string to an internal-error V6 failure.
pub(crate) fn internal(e: String) -> V6Error {
    V6Error::new(ErrorCode::Internal, e)
}

// ── optional field extractors (absent -> None; present-but-wrong-type -> error) ──

/// An optional integer field (`None` if absent/null, `invalid_field` if not an int).
pub(crate) fn opt_i64(data: &Value, field: &str) -> Result<Option<i64>, V6Error> {
    match data.get(field) {
        None | Some(Value::Null) => Ok(None),
        Some(v) => v.as_i64().map(Some).ok_or_else(|| {
            V6Error::field(
                ErrorCode::InvalidField,
                field,
                format!("{field} must be an integer"),
            )
        }),
    }
}

/// An optional string field.
pub(crate) fn opt_str<'a>(data: &'a Value, field: &str) -> Result<Option<&'a str>, V6Error> {
    match data.get(field) {
        None | Some(Value::Null) => Ok(None),
        Some(v) => v.as_str().map(Some).ok_or_else(|| {
            V6Error::field(
                ErrorCode::InvalidField,
                field,
                format!("{field} must be a string"),
            )
        }),
    }
}

/// An optional boolean field.
pub(crate) fn opt_bool(data: &Value, field: &str) -> Result<Option<bool>, V6Error> {
    match data.get(field) {
        None | Some(Value::Null) => Ok(None),
        Some(v) => v.as_bool().map(Some).ok_or_else(|| {
            V6Error::field(
                ErrorCode::InvalidField,
                field,
                format!("{field} must be a boolean"),
            )
        }),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use serde_json::json;

    #[test]
    fn page_args_clamp_huge_and_negative_to_i32_range() {
        // A huge (valid u64) offset must clamp, not wrap on the downstream `as i32`.
        let (offset, limit) =
            page_args(&json!({ "offset": 2_147_483_648_i64, "limit": -5 })).unwrap();
        assert_eq!(offset, i32::MAX as i64);
        assert_eq!(limit, 0); // negative limit clamps to 0 ("to the end")
        assert_eq!(offset as i32, i32::MAX); // no wrap
    }

    #[test]
    fn i32_saturating_does_not_wrap() {
        assert_eq!(i32_saturating(2_147_483_648), i32::MAX);
        assert_eq!(i32_saturating(-2_147_483_649), i32::MIN);
        assert_eq!(i32_saturating(42), 42);
    }

    /// The laziest request a client can write used to mean "read every tag in
    /// the library and put it in one frame".
    #[test]
    fn an_absent_limit_is_a_page_not_the_whole_library() {
        let (offset, limit) = page_args(&json!({})).unwrap();
        assert_eq!(offset, 0);
        assert_eq!(limit, DEFAULT_PAGE_LIMIT);
    }

    /// The escape hatch stays: a caller that means everything says so.
    #[test]
    fn an_explicit_zero_limit_still_means_everything() {
        let (_, limit) = page_args(&json!({ "limit": 0 })).unwrap();
        assert_eq!(limit, 0);
    }

    #[test]
    fn capabilities_advertise_session_and_domain_ops_and_events() {
        let caps = capabilities();
        let ops: Vec<&str> = caps["ops"]
            .as_array()
            .unwrap()
            .iter()
            .map(|v| v.as_str().unwrap())
            .collect();
        for expected in [
            "handshake",
            "ping",
            "player_status",
            "system_info",
            "track_get",
            "library_tracks",
            "playlist_list",
        ] {
            assert!(
                ops.contains(&expected),
                "capabilities.ops missing {expected}"
            );
        }
        let events: Vec<&str> = caps["events"]
            .as_array()
            .unwrap()
            .iter()
            .map(|v| v.as_str().unwrap())
            .collect();
        assert!(events.contains(&"volume_changed"));
    }
}
