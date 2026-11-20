//! V6 op dispatch.
//!
//! A lean parallel to the legacy [`commands`](super::commands) module: handlers
//! take `(&Value, &dyn Providers, ...)` and return typed V6 response `data` or a
//! typed error, with no `Ctx`/`WireCodec` (those carry V4 spellings). The
//! [`V6Session`](super::session_v6::V6Session) frames the envelope around the
//! returned value.

pub mod library;
pub mod library_sync;
pub mod media;
pub mod nowplaying;
pub mod nowplaying_list;
pub mod player;
pub mod playlist;
pub mod podcast;
pub mod system;
pub mod tags;
pub mod track;

use serde_json::{Value, json};

use mbrc_wire::v6::ErrorCode;

use crate::cover::store::CoverStore;
use crate::metadata_cache::MetadataCache;
use crate::nowplaying::NowPlayingCache;
use crate::providers::{HostError, Providers};
use crate::server::path_guard;

/// Ops the session answers itself, advertised in the handshake capabilities
/// alongside every domain's own list.
const SESSION_OPS: &[&str] = &["handshake", "ping", "pair"];

/// Event names the server may emit (best-effort). Advertised in capabilities so a
/// client knows what to expect; it stays in sync with `notifications_v6::build`.
pub const SUPPORTED_EVENTS: &[&str] = &[
    "play_state_changed",
    "volume_changed",
    "mute_changed",
    "shuffle_changed",
    "repeat_changed",
    "scrobbling_changed",
    "stop_after_current_changed",
    "now_playing_changed",
    "now_playing_lyrics_changed",
    "now_playing_cover_changed",
    "now_playing_tags_changed",
    "now_playing_list_changed",
    "cover_cache_changed",
    "library_changed",
    "media_changed",
    "library_artist_image_updated",
    crate::server::ARTIST_IMAGES_CLEARED,
    crate::power::CHANGED_EVENT,
    "server_shutdown",
    crate::server::permissions::gate::PERMISSIONS_CHANGED,
];

/// Optional behaviours this server offers beyond the op catalog, by name.
///
/// Empty today. A client treats a name it does not know as absent, so a feature
/// can be added without a protocol bump.
const FEATURES: &[&str] = &[];

/// The capability set advertised in the handshake response.
///
/// The ops a socket may send, the events it may receive and the optional
/// `features`; each domain appends its own `OPS`.
pub fn capabilities() -> Value {
    capabilities_with(SESSION_OPS)
}

/// The capability set `GET /api/v6/capabilities` serves: the same, without the
/// ops only a socket answers.
pub fn http_capabilities() -> Value {
    capabilities_with(&[])
}

fn capabilities_with(session_ops: &[&'static str]) -> Value {
    let ops: Vec<&str> = session_ops
        .iter()
        .chain(player::OPS)
        .chain(system::OPS)
        .chain(track::OPS)
        .chain(library::OPS)
        .chain(playlist::OPS)
        .chain(nowplaying::OPS)
        .chain(nowplaying_list::OPS)
        .chain(podcast::OPS)
        .chain(media::OPS)
        .chain(tags::OPS)
        .chain(crate::power::OPS)
        .copied()
        .collect();
    json!({ "ops": ops, "events": SUPPORTED_EVENTS, "features": FEATURES })
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
    if let Err(refused) = refuse_guarded_paths(data, providers, metadata_cache) {
        return Some(Err(refused));
    }
    player::dispatch(op, data, providers, now_playing)
        .or_else(|| system::dispatch(op, data, providers))
        .or_else(|| track::dispatch(op, data, providers, cover_store))
        .or_else(|| library::dispatch(op, data, providers, cover_store, metadata_cache))
        .or_else(|| playlist::dispatch(op, data, providers, metadata_cache, cover_store))
        .or_else(|| nowplaying::dispatch(op, data, providers, now_playing, cover_store))
        .or_else(|| podcast::dispatch(op, data, providers, cover_store))
        .or_else(|| media::dispatch(op, data, providers, cover_store, metadata_cache))
        .or_else(|| tags::dispatch(op, data, providers, metadata_cache))
        .or_else(|| {
            nowplaying_list::dispatch(
                op,
                data,
                providers,
                now_playing,
                cover_store,
                metadata_cache,
            )
        })
}

/// `invalid_field` when a request names a system path or a server the host does not use.
fn refuse_guarded_paths(
    data: &Value,
    providers: &dyn Providers,
    metadata_cache: Option<&MetadataCache>,
) -> Result<(), V6Error> {
    let named = path_guard::named_paths(data);
    let unknown = path_guard::first_refused(
        named.iter().map(|(_, path)| *path),
        providers,
        metadata_cache.map(MetadataCache::known_servers),
    );
    let Some(unknown) = unknown else {
        return Ok(());
    };
    let field = named
        .iter()
        .find_map(|(field, path)| (*path == unknown).then_some(*field))
        .unwrap_or("path");
    Err(V6Error::field(
        ErrorCode::InvalidField,
        field,
        format!("{unknown} is a system path, or on a network server this library does not use"),
    ))
}

/// The shared V6 pagination envelope: `{ total, offset, items }` (#118 §8). `total`
/// is the full count; `items` is the served page (its length conveys the limit).
pub(crate) fn page_json(total: usize, offset: i64, items: Vec<Value>) -> Value {
    json!({ "total": total, "offset": offset, "items": items })
}

/// The page size served when a request names none, or names `0`.
pub(crate) const DEFAULT_PAGE_LIMIT: i64 = 1000;

/// The most rows one page serves, whatever `limit` asks for.
///
/// Every page has an end, so no request puts a whole library in one frame; a
/// client reads on until `offset + items.length` reaches `total`.
pub(crate) const MAX_PAGE_LIMIT: i64 = 5000;

/// `(offset, limit)` from a paginated request.
///
/// A negative value is `invalid_field`. `limit` absent or `0` is
/// [`DEFAULT_PAGE_LIMIT`] and is capped at [`MAX_PAGE_LIMIT`]; `offset` is capped
/// at `i32::MAX`, the widest index the providers take, so the `offset` echoed in
/// the response is always the one used.
pub(crate) fn page_args(data: &Value) -> Result<(i64, i64), V6Error> {
    let offset = non_negative(data, "offset")?
        .unwrap_or(0)
        .min(i32::MAX as i64);
    Ok((offset, page_limit(data)?))
}

/// The `limit` of a paged request, by the rule [`page_args`] describes.
pub(crate) fn page_limit(data: &Value) -> Result<i64, V6Error> {
    Ok(match non_negative(data, "limit")? {
        None | Some(0) => DEFAULT_PAGE_LIMIT,
        Some(limit) => limit.min(MAX_PAGE_LIMIT),
    })
}

/// An optional integer `field` that may not be negative.
fn non_negative(data: &Value, field: &str) -> Result<Option<i64>, V6Error> {
    match opt_i64(data, field)? {
        Some(n) if n < 0 => Err(V6Error::field(
            ErrorCode::InvalidField,
            field,
            format!("{field} may not be negative"),
        )),
        n => Ok(n),
    }
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

/// The V6 failure for a call into MusicBee that did not answer.
///
/// MusicBee declining or being out of reach is `unavailable`, a precondition a
/// client can show and retry; a failure is `internal_error`. A plain string is
/// the core's own failure.
pub(crate) fn host_error(e: impl Into<HostError>) -> V6Error {
    match e.into() {
        HostError::Refused(detail) => {
            tracing::debug!(%detail, "MusicBee declined a request");
            V6Error::new(ErrorCode::Unavailable, "MusicBee declined the request")
        }
        HostError::Unreachable(detail) => {
            tracing::debug!(%detail, "MusicBee is out of reach");
            V6Error::new(
                ErrorCode::Unavailable,
                "the plugin cannot reach MusicBee right now",
            )
        }
        HostError::Failed(detail) => V6Error::new(ErrorCode::Internal, detail),
    }
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
    fn a_huge_page_is_capped() {
        let (offset, limit) =
            page_args(&json!({ "offset": 2_147_483_648_i64, "limit": 1_000_000 })).unwrap();
        assert_eq!(offset, i32::MAX as i64);
        assert_eq!(limit, MAX_PAGE_LIMIT);
    }

    #[test]
    fn a_negative_offset_or_limit_is_invalid_field() {
        for field in ["offset", "limit"] {
            let err = page_args(&json!({ field: -1 })).unwrap_err();
            assert_eq!(err.code, ErrorCode::InvalidField);
            assert_eq!(err.field.as_deref(), Some(field));
        }
    }

    /// MusicBee declining or being out of reach is a precondition a client can
    /// show; only a failure is `internal_error`, and only it carries its detail.
    #[test]
    fn a_host_error_reaches_the_client_as_its_kind() {
        let refused = host_error(HostError::Refused("execute_command Play".into()));
        assert_eq!(refused.code, ErrorCode::Unavailable);
        assert_eq!(refused.message, "MusicBee declined the request");

        let unreachable = host_error(HostError::Unreachable("callback is null".into()));
        assert_eq!(unreachable.code, ErrorCode::Unavailable);

        let failed = host_error(HostError::Failed("bad reply".into()));
        assert_eq!(failed.code, ErrorCode::Internal);
        assert_eq!(failed.message, "bad reply");

        assert_eq!(
            host_error("the core's own".to_string()).code,
            ErrorCode::Internal
        );
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

    #[test]
    fn a_zero_limit_is_the_default_page() {
        let (_, limit) = page_args(&json!({ "limit": 0 })).unwrap();
        assert_eq!(limit, DEFAULT_PAGE_LIMIT);
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
