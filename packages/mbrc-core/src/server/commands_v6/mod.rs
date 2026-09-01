//! V6 op dispatch. A lean parallel to the legacy [`commands`](super::commands)
//! module: handlers take `(&Value, &dyn Providers, ...)` and return typed V6
//! response `data` or a typed error - no `Ctx`/`WireCodec` (those carry V4
//! spellings). The [`V6Session`](super::session_v6::V6Session) frames the envelope
//! around the returned value.

pub mod player;
pub mod system;

use serde_json::{json, Value};

use mbrc_wire::v6::ErrorCode;

use crate::cover::store::CoverStore;
use crate::metadata_cache::MetadataCache;
use crate::nowplaying::NowPlayingCache;
use crate::providers::Providers;

/// Ops handled by the session itself (the spine), advertised alongside the domain
/// op lists in the handshake capabilities.
const SPINE_OPS: &[&str] = &["handshake", "ping"];

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
];

/// The capability set advertised in the handshake response: the ops the server
/// accepts and the events it may emit. Added additively (#118 §9 Q5) - clients
/// tolerate its absence. Each new domain appends its `OPS` here.
pub fn capabilities() -> Value {
    let ops: Vec<&str> = SPINE_OPS
        .iter()
        .chain(player::OPS)
        .chain(system::OPS)
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
}

impl V6Error {
    pub fn new(code: ErrorCode, message: impl Into<String>) -> Self {
        Self {
            code,
            message: message.into(),
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
    _cover_store: Option<&CoverStore>,
    _metadata_cache: Option<&MetadataCache>,
) -> Option<OpResult> {
    player::dispatch(op, data, providers, now_playing)
        .or_else(|| system::dispatch(op, data, providers))
}

// ── shared field extractors (typed, with the right error code) ──────────────

/// A required integer field (`missing_field` if absent, `invalid_field` if not an int).
pub(crate) fn req_i64(data: &Value, field: &str) -> Result<i64, V6Error> {
    match data.get(field) {
        None => Err(V6Error::new(
            ErrorCode::MissingField,
            format!("missing required field: {field}"),
        )),
        Some(v) => v.as_i64().ok_or_else(|| {
            V6Error::new(
                ErrorCode::InvalidField,
                format!("{field} must be an integer"),
            )
        }),
    }
}

/// A required boolean field.
pub(crate) fn req_bool(data: &Value, field: &str) -> Result<bool, V6Error> {
    match data.get(field) {
        None => Err(V6Error::new(
            ErrorCode::MissingField,
            format!("missing required field: {field}"),
        )),
        Some(v) => v.as_bool().ok_or_else(|| {
            V6Error::new(
                ErrorCode::InvalidField,
                format!("{field} must be a boolean"),
            )
        }),
    }
}

/// A required string field.
pub(crate) fn req_str<'a>(data: &'a Value, field: &str) -> Result<&'a str, V6Error> {
    match data.get(field) {
        None => Err(V6Error::new(
            ErrorCode::MissingField,
            format!("missing required field: {field}"),
        )),
        Some(v) => v.as_str().ok_or_else(|| {
            V6Error::new(ErrorCode::InvalidField, format!("{field} must be a string"))
        }),
    }
}

/// Map a provider/FFI failure string to an internal-error V6 failure.
pub(crate) fn internal(e: String) -> V6Error {
    V6Error::new(ErrorCode::Internal, e)
}

// ── optional field extractors (absent -> None; present-but-wrong-type -> error) ──
