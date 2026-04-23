use std::sync::Arc;

use tracing::{info, warn};

use crate::ffi::types::QueryType;
use crate::protocol::constants;
use crate::protocol::messages::SocketMessage;
use crate::server::{PlayerStateResponse, TrackInfoResponse};
use crate::state::AppState;

/// Dispatch an authenticated command and return zero or more response messages.
///
/// - Fire-and-forget actions (play/pause, stop, next, previous) return an empty vec.
/// - Query commands return a single-element vec.
/// - The `init` command returns multiple messages (track, cover, lyrics, player status).
/// - Unhandled commands are logged and return an empty vec.
pub async fn dispatch_command(
    context: &str,
    data: &serde_json::Value,
    state: &Arc<AppState>,
) -> Vec<SocketMessage> {
    match context {
        // ── Player actions (fire-and-forget thin callbacks) ──────────
        constants::PLAYER_PLAY_PAUSE | constants::PLAYER_PLAY | constants::PLAYER_PAUSE => {
            let s = Arc::clone(state);
            let _ = tokio::task::spawn_blocking(move || s.callbacks().player_play_pause()).await;
            vec![]
        }

        constants::PLAYER_STOP => {
            let s = Arc::clone(state);
            let _ = tokio::task::spawn_blocking(move || s.callbacks().player_stop()).await;
            vec![]
        }

        constants::PLAYER_NEXT => {
            let s = Arc::clone(state);
            let _ = tokio::task::spawn_blocking(move || s.callbacks().player_next()).await;
            vec![]
        }

        constants::PLAYER_PREVIOUS => {
            let s = Arc::clone(state);
            let _ = tokio::task::spawn_blocking(move || s.callbacks().player_previous()).await;
            vec![]
        }

        // ── Volume (get/set hybrid) ─────────────────────────────────
        constants::PLAYER_VOLUME => handle_volume(data, state).await,

        // ── Position (get/set hybrid) ───────────────────────────────
        constants::NOW_PLAYING_POSITION => handle_position(data, state).await,

        // ── Read-only player state queries ──────────────────────────
        constants::PLAYER_STATE => {
            query_player_field(
                state,
                |ps| ps.play_state.clone().into(),
                constants::PLAYER_STATE,
            )
            .await
        }

        constants::PLAYER_STATUS => {
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || {
                s.callbacks()
                    .query_no_params::<PlayerStateResponse>(QueryType::PlayerState)
            })
            .await
            {
                Ok(Ok(ps)) => {
                    vec![SocketMessage::new(
                        constants::PLAYER_STATUS,
                        legacy_player_status_payload(&ps),
                    )]
                }
                Ok(Err(e)) => {
                    warn!("PlayerState query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        constants::PLAYER_MUTE => handle_mute(data, state).await,
        // TODO(golden-trace): `playershuffle` has a v2 boolean / v3+ string
        // branch documented in the rollout plan. Hold the set-path until a
        // golden trace pins the exact wire format we need to emit.
        constants::PLAYER_SHUFFLE => {
            query_player_field(
                state,
                |ps| ps.shuffle.clone().into(),
                constants::PLAYER_SHUFFLE,
            )
            .await
        }
        constants::PLAYER_REPEAT => handle_repeat(data, state).await,
        constants::PLAYER_SCROBBLE => handle_scrobble(data, state).await,
        constants::PLAYER_AUTO_DJ => handle_auto_dj(data, state).await,

        // ── Now playing queries ─────────────────────────────────────
        constants::NOW_PLAYING_TRACK => {
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || {
                s.callbacks()
                    .query_no_params::<TrackInfoResponse>(QueryType::TrackInfo)
            })
            .await
            {
                Ok(Ok(track)) => {
                    if let Ok(val) = serde_json::to_value(&track) {
                        vec![SocketMessage::new(constants::NOW_PLAYING_TRACK, val)]
                    } else {
                        vec![]
                    }
                }
                Ok(Err(e)) => {
                    warn!("TrackInfo query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        constants::NOW_PLAYING_COVER => {
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || {
                s.callbacks()
                    .query_no_params::<String>(QueryType::CoverData)
            })
            .await
            {
                Ok(Ok(cover)) => vec![SocketMessage::new(constants::NOW_PLAYING_COVER, cover)],
                Ok(Err(e)) => {
                    warn!("CoverData query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        constants::NOW_PLAYING_LYRICS => {
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || {
                s.callbacks().query_no_params::<String>(QueryType::Lyrics)
            })
            .await
            {
                Ok(Ok(lyrics)) => vec![SocketMessage::new(constants::NOW_PLAYING_LYRICS, lyrics)],
                Ok(Err(e)) => {
                    warn!("Lyrics query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        // ── Init (full state push) ──────────────────────────────────
        constants::INIT => handle_init(state).await,

        // ── Keep-alive ──────────────────────────────────────────────
        constants::PING => vec![SocketMessage::new(constants::PONG, serde_json::Value::Null)],
        constants::PONG => vec![],

        // ── Fire-and-forget commands (single-value payload) ─────────
        constants::PLAYLIST_PLAY => {
            if let Some(url) = parse_string_from_data(data) {
                let s = Arc::clone(state);
                let _ =
                    tokio::task::spawn_blocking(move || s.callbacks().playlist_play(&url)).await;
            }
            vec![]
        }

        constants::LIBRARY_PLAY_ALL => {
            let shuffle = parse_bool_from_data(data).unwrap_or(false);
            let s = Arc::clone(state);
            let _ =
                tokio::task::spawn_blocking(move || s.callbacks().library_play_all(shuffle)).await;
            vec![]
        }

        constants::PLAYER_OUTPUT_SWITCH => {
            if let Some(device) = parse_string_from_data(data) {
                let s = Arc::clone(state);
                let _ =
                    tokio::task::spawn_blocking(move || s.callbacks().output_switch(&device)).await;
            }
            vec![]
        }

        constants::NOW_PLAYING_LIST_PLAY => {
            if let Some(path) = parse_string_from_data(data) {
                let s = Arc::clone(state);
                let _ =
                    tokio::task::spawn_blocking(move || s.callbacks().now_playing_list_play(&path))
                        .await;
            }
            vec![]
        }

        constants::NOW_PLAYING_LIST_REMOVE => {
            if let Some(idx) = parse_int_from_data(data) {
                if idx >= 0 {
                    let s = Arc::clone(state);
                    let _ = tokio::task::spawn_blocking(move || {
                        s.callbacks().now_playing_list_remove(idx)
                    })
                    .await;
                }
            }
            vec![]
        }

        // ── Commands we know about but can't service yet ────────────
        constants::PLUGIN_VERSION => {
            vec![SocketMessage::new(constants::NOT_ALLOWED, "not available")]
        }

        // ── Unhandled commands ──────────────────────────────────────
        //
        // TODO(golden-trace): the remaining legacy commands — list
        // queries (playlistlist, nowplayinglist, radiostations,
        // playeroutput, nowplayingdetails, librarycovercachebuildstatus,
        // library{search,browse,artistalbums,genreartists,albumtracks}),
        // composite payloads (libraryqueue*, nowplayinglistsearch/move,
        // libraryalbumcover, nowplayingtagchange), rating setters, and
        // verifyconnection — are all backed by W2 callbacks but their
        // exact legacy wire shape must be pinned against a golden-trace
        // fixture before we commit to a response format.
        _ => {
            info!("Unhandled command: {}", context);
            vec![]
        }
    }
}

/// `playermute` hybrid. Any data value sets the mute flag; the current
/// mute state is always returned.
async fn handle_mute(data: &serde_json::Value, state: &Arc<AppState>) -> Vec<SocketMessage> {
    if let Some(value) = parse_bool_from_data(data) {
        let s = Arc::clone(state);
        let _ = tokio::task::spawn_blocking(move || s.callbacks().set_mute(value)).await;
    }
    query_player_field(state, |ps| ps.mute.into(), constants::PLAYER_MUTE).await
}

/// `playerrepeat` hybrid. Any string value sets the repeat mode; the
/// current mode is always returned.
async fn handle_repeat(data: &serde_json::Value, state: &Arc<AppState>) -> Vec<SocketMessage> {
    if let Some(mode) = parse_string_from_data(data) {
        let s = Arc::clone(state);
        let _ = tokio::task::spawn_blocking(move || s.callbacks().set_repeat(&mode)).await;
    }
    query_player_field(
        state,
        |ps| ps.repeat.clone().into(),
        constants::PLAYER_REPEAT,
    )
    .await
}

/// `scrobbler` hybrid. Any data value sets scrobble; current state returned.
async fn handle_scrobble(data: &serde_json::Value, state: &Arc<AppState>) -> Vec<SocketMessage> {
    if let Some(value) = parse_bool_from_data(data) {
        let s = Arc::clone(state);
        let _ = tokio::task::spawn_blocking(move || s.callbacks().set_scrobble(value)).await;
    }
    query_player_field(state, |ps| ps.scrobble.into(), constants::PLAYER_SCROBBLE).await
}

/// `playerautodj` — set-only in the W2 surface. The current C# legacy
/// handler mirrors the chosen value back; we do the same by echoing.
async fn handle_auto_dj(data: &serde_json::Value, state: &Arc<AppState>) -> Vec<SocketMessage> {
    match parse_bool_from_data(data) {
        Some(value) => {
            let s = Arc::clone(state);
            let _ = tokio::task::spawn_blocking(move || s.callbacks().set_auto_dj(value)).await;
            vec![SocketMessage::new(constants::PLAYER_AUTO_DJ, value)]
        }
        None => vec![],
    }
}

/// Get/set volume. Empty or default data → return current volume. Otherwise set it.
async fn handle_volume(data: &serde_json::Value, state: &Arc<AppState>) -> Vec<SocketMessage> {
    // If data contains a numeric value, set volume first
    if let Some(vol) = parse_int_from_data(data) {
        let s = Arc::clone(state);
        let _ = tokio::task::spawn_blocking(move || s.callbacks().player_set_volume(vol)).await;
    }

    // Always return current volume
    query_player_field(state, |ps| ps.volume.into(), constants::PLAYER_VOLUME).await
}

/// Get/set position. Empty or default data → return current position. Otherwise set it.
async fn handle_position(data: &serde_json::Value, state: &Arc<AppState>) -> Vec<SocketMessage> {
    // If data contains a numeric value, set position first
    if let Some(pos) = parse_int_from_data(data) {
        let s = Arc::clone(state);
        let _ = tokio::task::spawn_blocking(move || s.callbacks().player_set_position(pos)).await;
    }

    // Always return current position
    query_player_field(
        state,
        |ps| ps.position.into(),
        constants::NOW_PLAYING_POSITION,
    )
    .await
}

/// Full state push: queries all 4 types and returns multiple messages.
async fn handle_init(state: &Arc<AppState>) -> Vec<SocketMessage> {
    let mut messages = Vec::new();

    // Query track info
    let s = Arc::clone(state);
    if let Ok(Ok(track)) = tokio::task::spawn_blocking(move || {
        s.callbacks()
            .query_no_params::<TrackInfoResponse>(QueryType::TrackInfo)
    })
    .await
    {
        if let Ok(val) = serde_json::to_value(&track) {
            messages.push(SocketMessage::new(constants::NOW_PLAYING_TRACK, val));
        }
    }

    // Query cover art
    let s = Arc::clone(state);
    if let Ok(Ok(cover)) = tokio::task::spawn_blocking(move || {
        s.callbacks()
            .query_no_params::<String>(QueryType::CoverData)
    })
    .await
    {
        messages.push(SocketMessage::new(constants::NOW_PLAYING_COVER, cover));
    }

    // Query lyrics
    let s = Arc::clone(state);
    if let Ok(Ok(lyrics)) = tokio::task::spawn_blocking(move || {
        s.callbacks().query_no_params::<String>(QueryType::Lyrics)
    })
    .await
    {
        messages.push(SocketMessage::new(constants::NOW_PLAYING_LYRICS, lyrics));
    }

    // Query player state (sent as playerstatus)
    let s = Arc::clone(state);
    if let Ok(Ok(ps)) = tokio::task::spawn_blocking(move || {
        s.callbacks()
            .query_no_params::<PlayerStateResponse>(QueryType::PlayerState)
    })
    .await
    {
        if let Ok(val) = serde_json::to_value(&ps) {
            messages.push(SocketMessage::new(constants::PLAYER_STATUS, val));
        }
    }

    messages
}

/// Helper: query PlayerState and extract a single field as a JSON value.
async fn query_player_field(
    state: &Arc<AppState>,
    extract: fn(&PlayerStateResponse) -> serde_json::Value,
    context: &str,
) -> Vec<SocketMessage> {
    let s = Arc::clone(state);
    match tokio::task::spawn_blocking(move || {
        s.callbacks()
            .query_no_params::<PlayerStateResponse>(QueryType::PlayerState)
    })
    .await
    {
        Ok(Ok(ps)) => vec![SocketMessage::new(context, extract(&ps))],
        Ok(Err(e)) => {
            warn!("{} query failed: {}", context, e);
            vec![]
        }
        Err(e) => {
            warn!("spawn_blocking panicked: {}", e);
            vec![]
        }
    }
}

/// Parse an integer from the message data field.
/// Supports: integer literal, string containing an integer, or nested value.
fn parse_int_from_data(data: &serde_json::Value) -> Option<i32> {
    // Direct integer
    if let Some(n) = data.as_i64() {
        return Some(n as i32);
    }
    // String containing an integer
    if let Some(s) = data.as_str() {
        if !s.is_empty() {
            return s.parse::<i32>().ok();
        }
    }
    None
}

/// Extract a non-empty string from the data field. Legacy clients send
/// command payloads either inline (`"data": "foo"`) or wrapped, so we
/// accept both.
fn parse_string_from_data(data: &serde_json::Value) -> Option<String> {
    if let Some(s) = data.as_str() {
        if !s.is_empty() {
            return Some(s.to_owned());
        }
    }
    None
}

/// Extract a boolean from the data field. Legacy clients send mute /
/// scrobble toggles either as a JSON boolean, a 0/1 integer, or the
/// strings `"true"`/`"false"`/`"toggle"`. We only honour an explicit
/// bool here; toggle semantics belong in the C# adapter.
fn parse_bool_from_data(data: &serde_json::Value) -> Option<bool> {
    if let Some(b) = data.as_bool() {
        return Some(b);
    }
    if let Some(n) = data.as_i64() {
        return Some(n != 0);
    }
    if let Some(s) = data.as_str() {
        return match s.to_ascii_lowercase().as_str() {
            "true" => Some(true),
            "false" => Some(false),
            _ => None,
        };
    }
    None
}

/// Build the legacy-wire `playerstatus` payload.
///
/// The wire format predates the internal `PlayerStateResponse` naming
/// and must match byte-for-byte against golden traces: keys are
/// concatenated (`playermute`, not `mute`) and `volume` serializes as
/// a string. V4 is the only supported protocol (see
/// `MIN_SUPPORTED_PROTOCOL`) so `shuffle` is always the string form.
fn legacy_player_status_payload(ps: &PlayerStateResponse) -> serde_json::Value {
    serde_json::json!({
        "playermute": ps.mute,
        "playerrepeat": ps.repeat,
        "playershuffle": ps.shuffle,
        "playerstate": ps.play_state,
        "playervolume": ps.volume.to_string(),
        "scrobbler": ps.scrobble,
    })
}
