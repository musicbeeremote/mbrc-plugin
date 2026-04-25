// extern "C" FFI functions that take raw pointers are the entire point of this
// crate — the C# host dereferences them on our behalf. Marking each fn `unsafe`
// would change the Rust-side ABI exposed to tests and break the pattern
// csbindgen mirrors into C#. Scope the allow to the crate root.
#![allow(clippy::not_unsafe_ptr_arg_deref)]

mod config;
mod discovery;
mod ffi;
mod logging;
mod protocol;
mod server;
mod state;

#[doc(hidden)]
pub mod replay_support;

// Re-export DTOs so drift-guard integration tests can construct and
// round-trip them without touching internal module paths.
pub use ffi::dtos::{
    AlbumCoverParams, BrowseParams, IndexParams, LibraryQueueParams, MoveParams, PaginationParams,
    QueryParams, SetBoolParams, SetLfmRatingParams, SetRepeatParams, StringValueParams,
};
pub use server::{
    AlbumCoverBatchEntry, AlbumCoverBatchResponse, AlbumCoverResponse, AlbumDto, AlbumListResponse,
    ArtistDto, ArtistListResponse, CoverCacheBuildStatusResponse, GenreDto, GenreListResponse,
    NowPlayingDetailsResponse, NowPlayingListResponse, NowPlayingTrackDto, OutputDevicesResponse,
    PlaybackPositionResponse, PlayerStateResponse, PlaylistDto, PlaylistListResponse,
    RadioStationDto, RadioStationsResponse, TrackDto, TrackInfoResponse, TrackListResponse,
};

use std::ffi::{c_char, c_int, CStr};
use std::sync::Arc;

use tracing::{info, warn};

use ffi::callbacks::SafeCallbacks;
use ffi::types::{MbrcCallbacks, MbrcResult, NotificationType, QueryType};
use protocol::constants;
use protocol::messages::{BroadcastEvent, SocketMessage};
use state::MbrcCore;

/// Initialize the Rust core with callbacks and storage path.
///
/// Must be called exactly once before any other `mbrc_*` function.
/// `storage_path` must be a valid null-terminated UTF-8 string.
/// `callbacks` is copied — the caller can free it after this returns.
///
/// Returns: 0 on success, negative error code on failure.
#[no_mangle]
pub extern "C" fn mbrc_initialize(callbacks: MbrcCallbacks, storage_path: *const c_char) -> c_int {
    // Validate storage_path
    if storage_path.is_null() {
        return MbrcResult::NullPointer as c_int;
    }

    let storage = match unsafe { CStr::from_ptr(storage_path) }.to_str() {
        Ok(s) => s.to_owned(),
        Err(_) => return MbrcResult::InvalidArgument as c_int,
    };

    // Initialize logging first so subsequent operations are logged
    logging::init(&storage);

    info!(storage_path = %storage, "mbrc_initialize called");

    let safe_callbacks = SafeCallbacks::new(callbacks);

    match MbrcCore::initialize(safe_callbacks, storage) {
        Ok(()) => MbrcResult::Ok as c_int,
        Err(e) => e as c_int,
    }
}

/// Shut down the Rust core. Stops networking and releases resources.
///
/// After this call, no other `mbrc_*` functions should be called.
/// Returns: 0 on success, negative error code on failure.
#[no_mangle]
pub extern "C" fn mbrc_shutdown() -> c_int {
    info!("mbrc_shutdown called");

    match MbrcCore::get() {
        Ok(core) => {
            core.shutdown();
            MbrcResult::Ok as c_int
        }
        Err(e) => e as c_int,
    }
}

/// Start the HTTP server. The listening port is read from
/// `core_settings.json` in the storage directory passed to
/// `mbrc_initialize` (falling back to the documented default if the
/// file is missing or invalid). C# WinForms is the only writer of
/// that file — Rust never persists settings.
///
/// Requires `mbrc_initialize` to have been called first.
/// Returns: 0 on success, negative error code on failure.
#[no_mangle]
pub extern "C" fn mbrc_start_networking() -> c_int {
    info!("mbrc_start_networking called");

    let core = match MbrcCore::get() {
        Ok(c) => c,
        Err(e) => return e as c_int,
    };

    let settings = config::CoreSettings::load_from_storage(core.state().storage_path());
    info!(port = settings.port, "starting hybrid server");

    match core.start_networking(settings.port) {
        Ok(()) => MbrcResult::Ok as c_int,
        Err(e) => e as c_int,
    }
}

/// Stop the HTTP server gracefully.
///
/// Returns: 0 on success, negative error code on failure.
#[no_mangle]
pub extern "C" fn mbrc_stop_networking() -> c_int {
    info!("mbrc_stop_networking called");

    match MbrcCore::get() {
        Ok(core) => match core.stop_networking() {
            Ok(()) => MbrcResult::Ok as c_int,
            Err(e) => e as c_int,
        },
        Err(e) => e as c_int,
    }
}

/// Forward a MusicBee notification to the Rust core.
///
/// `notification_type` maps to `NotificationType` enum values.
/// Currently used to trigger state refresh in the Rust side.
///
/// Returns: 0 on success, negative error code on failure.
#[no_mangle]
pub extern "C" fn mbrc_handle_notification(notification_type: c_int) -> c_int {
    // Convert the raw i32 to our enum
    let _notification = match notification_type {
        0 => NotificationType::TrackChanged,
        1 => NotificationType::PlayStateChanged,
        2 => NotificationType::VolumeLevelChanged,
        3 => NotificationType::VolumeMuteChanged,
        4 => NotificationType::NowPlayingLyricsReady,
        5 => NotificationType::NowPlayingArtworkReady,
        6 => NotificationType::NowPlayingListChanged,
        7 => NotificationType::FileAddedToLibrary,
        _ => return MbrcResult::InvalidArgument as c_int,
    };

    match MbrcCore::get() {
        Ok(core) => {
            info!(?_notification, "Received notification");
            let state = Arc::clone(core.state());
            let notification = _notification;
            core.runtime().spawn(async move {
                if let Some(event) = build_broadcast(notification, &state).await {
                    // Ignore send error — means no receivers are subscribed
                    let _ = state.event_tx().send(event);
                }
            });
            MbrcResult::Ok as c_int
        }
        Err(e) => e as c_int,
    }
}

/// Free a string previously returned by the Rust core.
///
/// The pointer must have been allocated by Rust (e.g. via `CString::into_raw`).
/// Passing a null pointer is a no-op.
#[no_mangle]
pub extern "C" fn mbrc_free_string(ptr: *mut c_char) {
    if !ptr.is_null() {
        unsafe {
            let _ = std::ffi::CString::from_raw(ptr);
        }
    }
}

/// Replay-harness-visible alias for [`build_broadcast`] — same impl,
/// just `pub(crate)` so `replay_support` can re-export it. Production
/// code paths still go through `build_broadcast`.
#[doc(hidden)]
pub(crate) async fn build_broadcast_for_replay(
    notification: NotificationType,
    state: &Arc<state::AppState>,
) -> Option<BroadcastEvent> {
    build_broadcast(notification, state).await
}

/// Build a broadcast event for a given notification by querying MusicBee via callbacks.
/// Returns `None` for notifications that don't produce broadcasts (e.g. FileAddedToLibrary).
async fn build_broadcast(
    notification: NotificationType,
    state: &Arc<state::AppState>,
) -> Option<BroadcastEvent> {
    match notification {
        NotificationType::TrackChanged => {
            // Mirror the legacy C# NotificationHandler.HandleTrackChanged
            // multi-frame fan-out: rating + lfm-rating + lyrics + position
            // + track. Lyrics is left empty — NowPlayingLyricsReady fires
            // its own broadcast once the lyrics actually load.
            let track_state = Arc::clone(state);
            let track = tokio::task::spawn_blocking(move || {
                track_state
                    .callbacks()
                    .query_no_params::<TrackInfoResponse>(QueryType::TrackInfo)
            })
            .await
            .ok()?
            .ok();

            let track = match track {
                Some(t) => t,
                None => {
                    warn!("Failed to query track info for TrackChanged broadcast");
                    return None;
                }
            };

            let pos_state = Arc::clone(state);
            let position = tokio::task::spawn_blocking(move || {
                pos_state
                    .callbacks()
                    .query_no_params::<PlaybackPositionResponse>(QueryType::PlaybackPosition)
            })
            .await
            .ok()
            .and_then(|r| r.ok())
            .unwrap_or(PlaybackPositionResponse {
                current: 0,
                total: 0,
            });

            let rating_state = Arc::clone(state);
            let rating = tokio::task::spawn_blocking(move || {
                rating_state
                    .callbacks()
                    .query_no_params::<String>(QueryType::NowPlayingRating)
            })
            .await
            .ok()
            .and_then(|r| r.ok())
            .unwrap_or_default();

            let lfm_state = Arc::clone(state);
            let lfm = tokio::task::spawn_blocking(move || {
                lfm_state
                    .callbacks()
                    .query_no_params::<String>(QueryType::NowPlayingLfmRating)
            })
            .await
            .ok()
            .and_then(|r| r.ok())
            .unwrap_or_default();

            // Wire order from captured Android-v4 traces:
            // rating -> lfm-rating -> lyrics -> position -> track.
            // Cover does NOT appear in TrackChanged bursts — it arrives
            // separately via NowPlayingArtworkReady once artwork loads.
            let messages = vec![
                SocketMessage::new(
                    constants::NOW_PLAYING_RATING,
                    serde_json::Value::String(rating),
                ),
                SocketMessage::new(
                    constants::NOW_PLAYING_LFM_RATING,
                    serde_json::Value::String(lfm),
                ),
                SocketMessage::new(
                    constants::NOW_PLAYING_LYRICS,
                    serde_json::Value::String(String::new()),
                ),
                SocketMessage::new(
                    constants::NOW_PLAYING_POSITION,
                    serde_json::to_value(&position).unwrap_or_default(),
                ),
                SocketMessage::new(
                    constants::NOW_PLAYING_TRACK,
                    serde_json::to_value(&track).unwrap_or_default(),
                ),
            ];
            Some(BroadcastEvent::multi(notification, messages))
        }

        NotificationType::PlayStateChanged => {
            let state2 = Arc::clone(state);
            let player = tokio::task::spawn_blocking(move || {
                state2
                    .callbacks()
                    .query_no_params::<PlayerStateResponse>(QueryType::PlayerState)
            })
            .await
            .ok()?
            .ok();

            match player {
                Some(p) => Some(BroadcastEvent::single(
                    notification,
                    constants::PLAYER_STATE,
                    serde_json::to_value(&p.play_state).unwrap_or_default(),
                )),
                None => {
                    warn!("Failed to query player state for PlayStateChanged broadcast");
                    None
                }
            }
        }

        NotificationType::VolumeLevelChanged => {
            let state2 = Arc::clone(state);
            let player = tokio::task::spawn_blocking(move || {
                state2
                    .callbacks()
                    .query_no_params::<PlayerStateResponse>(QueryType::PlayerState)
            })
            .await
            .ok()?
            .ok();

            match player {
                Some(p) => Some(BroadcastEvent::single(
                    notification,
                    constants::PLAYER_VOLUME,
                    serde_json::json!(p.volume),
                )),
                None => {
                    warn!("Failed to query player state for VolumeLevelChanged broadcast");
                    None
                }
            }
        }

        NotificationType::VolumeMuteChanged => {
            let state2 = Arc::clone(state);
            let player = tokio::task::spawn_blocking(move || {
                state2
                    .callbacks()
                    .query_no_params::<PlayerStateResponse>(QueryType::PlayerState)
            })
            .await
            .ok()?
            .ok();

            match player {
                Some(p) => Some(BroadcastEvent::single(
                    notification,
                    constants::PLAYER_MUTE,
                    serde_json::json!(p.mute),
                )),
                None => {
                    warn!("Failed to query player state for VolumeMuteChanged broadcast");
                    None
                }
            }
        }

        NotificationType::NowPlayingLyricsReady => {
            // Lyrics ready — broadcast empty context; actual lyrics fetched on demand
            Some(BroadcastEvent::single(
                notification,
                constants::NOW_PLAYING_LYRICS,
                serde_json::Value::String(String::new()),
            ))
        }

        NotificationType::NowPlayingArtworkReady => Some(BroadcastEvent::single(
            notification,
            constants::NOW_PLAYING_COVER,
            serde_json::Value::String(String::new()),
        )),

        NotificationType::NowPlayingListChanged => Some(BroadcastEvent::single(
            notification,
            constants::NOW_PLAYING_LIST_CHANGED,
            serde_json::Value::String(String::new()),
        )),

        NotificationType::FileAddedToLibrary => {
            // No broadcast for library file additions
            None
        }
    }
}
