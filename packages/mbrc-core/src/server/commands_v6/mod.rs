//! V6 op dispatch. A lean parallel to the legacy [`commands`](super::commands)
//! module: handlers take `(&Value, &dyn Providers, ...)` and return typed V6
//! response `data` or a typed error - no `Ctx`/`WireCodec` (those carry V4
//! spellings). The [`V6Session`](super::session_v6::V6Session) frames the envelope
//! around the returned value.

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
    let ops: Vec<&str> = SPINE_OPS.iter().copied().collect();
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
    cover_store: Option<&CoverStore>,
    metadata_cache: Option<&MetadataCache>,
) -> Option<OpResult> {
    let _ = (
        op,
        data,
        providers,
        now_playing,
        cover_store,
        metadata_cache,
    );
    None
}
