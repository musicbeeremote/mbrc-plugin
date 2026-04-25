use std::sync::Arc;

use tracing::{info, warn};

use crate::ffi::callbacks::LibraryQueueTarget;
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

        // ── Library queue (fire-and-forget with {query, type} payload) ──
        constants::LIBRARY_QUEUE_GENRE => {
            handle_library_queue(data, state, LibraryQueueTarget::Genre).await
        }
        constants::LIBRARY_QUEUE_ARTIST => {
            handle_library_queue(data, state, LibraryQueueTarget::Artist).await
        }
        constants::LIBRARY_QUEUE_ALBUM => {
            handle_library_queue(data, state, LibraryQueueTarget::Album).await
        }
        constants::LIBRARY_QUEUE_TRACK => {
            handle_library_queue(data, state, LibraryQueueTarget::Track).await
        }

        // ── Now playing list mutations ──────────────────────────────
        constants::NOW_PLAYING_LIST_MOVE => {
            if let (Some(from), Some(to)) = (
                data.get("from").and_then(|v| v.as_i64()).map(|n| n as i32),
                data.get("to").and_then(|v| v.as_i64()).map(|n| n as i32),
            ) {
                let s = Arc::clone(state);
                let _ = tokio::task::spawn_blocking(move || {
                    s.callbacks().now_playing_list_move(from, to)
                })
                .await;
            }
            vec![]
        }

        constants::NOW_PLAYING_LIST_SEARCH => {
            // Fixture shows the payload is a plain string query (`"who"`).
            if let Some(query) = parse_string_from_data(data) {
                let s = Arc::clone(state);
                let _ = tokio::task::spawn_blocking(move || {
                    s.callbacks().now_playing_list_search(&query)
                })
                .await;
            }
            vec![]
        }

        // ── Tag change (fire-and-forget, C# server sends no echo) ────
        //
        // TODO(w2): there is no `CommandType::NowPlayingTagChange` in the
        // FFI yet, so we acknowledge the frame at the wire level but can't
        // propagate the state change to the host. Matches current C#
        // silent-response behavior on the wire; the state gap is tracked
        // separately under the W2 surface.
        constants::NOW_PLAYING_TAG_CHANGE => vec![],

        // ── Simple no-param queries ─────────────────────────────────
        constants::PLAYER_OUTPUT => {
            simple_query(state, QueryType::OutputDevices, constants::PLAYER_OUTPUT, |r: crate::server::OutputDevicesResponse| {
                serde_json::json!({ "active": r.active, "devices": r.devices })
            })
            .await
        }

        constants::NOW_PLAYING_DETAILS => {
            simple_query(state, QueryType::NowPlayingDetails, constants::NOW_PLAYING_DETAILS, |r: crate::server::NowPlayingDetailsResponse| {
                // Flat camelCase object — serde rename rules on the DTO
                // already produce the exact wire keys (`albumArtist`,
                // `trackNo`, etc.).
                serde_json::to_value(&r).unwrap_or(serde_json::Value::Null)
            })
            .await
        }

        constants::LIBRARY_COVER_CACHE_BUILD_STATUS => {
            simple_query(state, QueryType::CoverCacheBuildStatus, constants::LIBRARY_COVER_CACHE_BUILD_STATUS, |r: crate::server::CoverCacheBuildStatusResponse| {
                serde_json::json!({ "building": r.building })
            })
            .await
        }

        // ── Paginated list queries (data/limit/offset/total envelope) ──
        constants::PLAYLIST_LIST => {
            let (offset, limit) = parse_pagination(data);
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().query_playlists()).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::PLAYLIST_LIST,
                    paginated_envelope(r.playlists, offset, limit),
                )],
                Ok(Err(e)) => {
                    warn!("PlaylistList query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        constants::NOW_PLAYING_LIST => {
            let (offset, limit) = parse_pagination(data);
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().query_now_playing_list(offset, limit)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::NOW_PLAYING_LIST,
                    paginated_envelope(r.tracks, offset, limit),
                )],
                Ok(Err(e)) => {
                    warn!("NowPlayingList query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        constants::RADIO_STATIONS => {
            let (offset, limit) = parse_pagination(data);
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().query_radio_stations(offset, limit)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::RADIO_STATIONS,
                    paginated_envelope(r.stations, offset, limit),
                )],
                Ok(Err(e)) => {
                    warn!("RadioStations query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        constants::LIBRARY_BROWSE_GENRES => {
            let (offset, limit) = parse_pagination(data);
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().library_browse_genres(offset, limit)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::LIBRARY_BROWSE_GENRES,
                    paginated_envelope(r.genres, offset, limit),
                )],
                Ok(Err(e)) => {
                    warn!("LibraryBrowseGenres query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        constants::LIBRARY_BROWSE_ARTISTS => {
            let (offset, limit) = parse_pagination(data);
            let album_artists = data
                .get("album_artists")
                .and_then(|v| v.as_bool())
                .unwrap_or(false);
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || {
                s.callbacks().library_browse_artists(offset, limit, album_artists)
            })
            .await
            {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::LIBRARY_BROWSE_ARTISTS,
                    paginated_envelope(r.artists, offset, limit),
                )],
                Ok(Err(e)) => {
                    warn!("LibraryBrowseArtists query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        constants::LIBRARY_BROWSE_ALBUMS => {
            let (offset, limit) = parse_pagination(data);
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().library_browse_albums(offset, limit)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::LIBRARY_BROWSE_ALBUMS,
                    paginated_envelope(r.albums, offset, limit),
                )],
                Ok(Err(e)) => {
                    warn!("LibraryBrowseAlbums query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        constants::LIBRARY_BROWSE_TRACKS => {
            let (offset, limit) = parse_pagination(data);
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().library_browse_tracks(offset, limit)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::LIBRARY_BROWSE_TRACKS,
                    paginated_envelope(r.tracks, offset, limit),
                )],
                Ok(Err(e)) => {
                    warn!("LibraryBrowseTracks query failed: {}", e);
                    vec![]
                }
                Err(e) => {
                    warn!("spawn_blocking panicked: {}", e);
                    vec![]
                }
            }
        }

        // ── Bare-array drill-down queries (no envelope) ─────────────
        // Request payload is a plain string (e.g. `"data": "AC/DC"`).
        // Response `data` is a bare JSON array of DTO entries.
        constants::LIBRARY_ARTIST_ALBUMS => {
            let artist = parse_string_from_data(data).unwrap_or_default();
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().library_artist_albums(&artist)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::LIBRARY_ARTIST_ALBUMS,
                    serde_json::to_value(&r.albums).unwrap_or(serde_json::Value::Array(vec![])),
                )],
                Ok(Err(e)) => { warn!("LibraryArtistAlbums query failed: {}", e); vec![] }
                Err(e) => { warn!("spawn_blocking panicked: {}", e); vec![] }
            }
        }

        constants::LIBRARY_GENRE_ARTISTS => {
            let genre = parse_string_from_data(data).unwrap_or_default();
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().library_genre_artists(&genre)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::LIBRARY_GENRE_ARTISTS,
                    serde_json::to_value(&r.artists).unwrap_or(serde_json::Value::Array(vec![])),
                )],
                Ok(Err(e)) => { warn!("LibraryGenreArtists query failed: {}", e); vec![] }
                Err(e) => { warn!("spawn_blocking panicked: {}", e); vec![] }
            }
        }

        constants::LIBRARY_ALBUM_TRACKS => {
            let album = parse_string_from_data(data).unwrap_or_default();
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().library_album_tracks(&album)).await {
                Ok(Ok(r)) => {
                    // Drill-down wire shape omits the `album` and `genre`
                    // columns that the shared TrackDto carries — the
                    // caller already knows the album, and genre isn't
                    // part of the legacy album-tracks frame.
                    let arr: Vec<serde_json::Value> = r.tracks.iter().map(|t| serde_json::json!({
                        "album_artist": t.album_artist,
                        "artist": t.artist,
                        "disc": t.disc,
                        "src": t.src,
                        "title": t.title,
                        "trackno": t.trackno,
                    })).collect();
                    vec![SocketMessage::new(
                        constants::LIBRARY_ALBUM_TRACKS,
                        serde_json::Value::Array(arr),
                    )]
                }
                Ok(Err(e)) => { warn!("LibraryAlbumTracks query failed: {}", e); vec![] }
                Err(e) => { warn!("spawn_blocking panicked: {}", e); vec![] }
            }
        }

        constants::LIBRARY_SEARCH_ARTIST => {
            let query = parse_string_from_data(data).unwrap_or_default();
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().library_search_artists(&query)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::LIBRARY_SEARCH_ARTIST,
                    serde_json::to_value(&r.artists).unwrap_or(serde_json::Value::Array(vec![])),
                )],
                Ok(Err(e)) => { warn!("LibrarySearchArtist query failed: {}", e); vec![] }
                Err(e) => { warn!("spawn_blocking panicked: {}", e); vec![] }
            }
        }

        constants::LIBRARY_SEARCH_ALBUM => {
            let query = parse_string_from_data(data).unwrap_or_default();
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().library_search_albums(&query)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::LIBRARY_SEARCH_ALBUM,
                    serde_json::to_value(&r.albums).unwrap_or(serde_json::Value::Array(vec![])),
                )],
                Ok(Err(e)) => { warn!("LibrarySearchAlbum query failed: {}", e); vec![] }
                Err(e) => { warn!("spawn_blocking panicked: {}", e); vec![] }
            }
        }

        constants::LIBRARY_SEARCH_GENRE => {
            let query = parse_string_from_data(data).unwrap_or_default();
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().library_search_genres(&query)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::LIBRARY_SEARCH_GENRE,
                    serde_json::to_value(&r.genres).unwrap_or(serde_json::Value::Array(vec![])),
                )],
                Ok(Err(e)) => { warn!("LibrarySearchGenre query failed: {}", e); vec![] }
                Err(e) => { warn!("spawn_blocking panicked: {}", e); vec![] }
            }
        }

        // ── Album-cover lookup (two wire variants) ──────────────────
        //
        // Android v4: `{"artist":"…","album":"…"}` → single-cover
        // response `{cover,hash,status}`. Empty album shortcuts to
        // `{"status":400}` without a callback.
        //
        // iOS v4: `{"offset":N,"limit":M}` → paginated batch response
        // `{data:[{album,artist,cover,hash,status},…],offset,limit,
        // total}`. Routes through QueryType::AlbumCoverBatch which
        // walks the C# cover cache by index.
        //
        // The two shapes are mutually exclusive — `album` and `offset`
        // never coexist in real captures — so we sniff on the
        // `offset`/`limit` keys to dispatch.
        constants::LIBRARY_ALBUM_COVER => {
            let is_batch = data.get("offset").is_some() || data.get("limit").is_some();
            if is_batch {
                let (offset, limit) = parse_pagination(data);
                let s = Arc::clone(state);
                match tokio::task::spawn_blocking(move || {
                    s.callbacks().query_album_cover_batch(offset, limit)
                })
                .await
                {
                    Ok(Ok(r)) => vec![SocketMessage::new(
                        constants::LIBRARY_ALBUM_COVER,
                        serde_json::to_value(&r).unwrap_or(serde_json::json!({
                            "data": [], "offset": offset, "limit": limit, "total": 0,
                        })),
                    )],
                    Ok(Err(e)) => {
                        warn!("AlbumCoverBatch query failed: {}", e);
                        vec![SocketMessage::new(
                            constants::LIBRARY_ALBUM_COVER,
                            serde_json::json!({
                                "data": [], "offset": offset, "limit": limit, "total": 0,
                            }),
                        )]
                    }
                    Err(e) => { warn!("spawn_blocking panicked: {}", e); vec![] }
                }
            } else {
                let artist = data.get("artist").and_then(|v| v.as_str()).unwrap_or("").to_owned();
                let album = data.get("album").and_then(|v| v.as_str()).unwrap_or("").to_owned();
                let client_hash = data.get("hash").and_then(|v| v.as_str()).unwrap_or("").to_owned();
                if album.is_empty() {
                    vec![SocketMessage::new(
                        constants::LIBRARY_ALBUM_COVER,
                        serde_json::json!({ "status": 400 }),
                    )]
                } else {
                    let s = Arc::clone(state);
                    match tokio::task::spawn_blocking(move || {
                        s.callbacks().query_album_cover(&artist, &album, &client_hash)
                    })
                    .await
                    {
                        Ok(Ok(r)) => vec![SocketMessage::new(
                            constants::LIBRARY_ALBUM_COVER,
                            serde_json::json!({
                                "cover": r.cover,
                                "hash": r.hash,
                                "status": r.status,
                            }),
                        )],
                        Ok(Err(e)) => {
                            warn!("AlbumCover query failed: {}", e);
                            vec![SocketMessage::new(
                                constants::LIBRARY_ALBUM_COVER,
                                serde_json::json!({ "status": 404 }),
                            )]
                        }
                        Err(e) => { warn!("spawn_blocking panicked: {}", e); vec![] }
                    }
                }
            }
        }

        constants::LIBRARY_SEARCH_TITLE => {
            let query = parse_string_from_data(data).unwrap_or_default();
            let s = Arc::clone(state);
            match tokio::task::spawn_blocking(move || s.callbacks().library_search_titles(&query)).await {
                Ok(Ok(r)) => vec![SocketMessage::new(
                    constants::LIBRARY_SEARCH_TITLE,
                    serde_json::to_value(&r.tracks).unwrap_or(serde_json::Value::Array(vec![])),
                )],
                Ok(Err(e)) => { warn!("LibrarySearchTitle query failed: {}", e); vec![] }
                Err(e) => { warn!("spawn_blocking panicked: {}", e); vec![] }
            }
        }

        // ── Commands we know about but can't service yet ────────────
        constants::PLUGIN_VERSION => {
            vec![SocketMessage::new(constants::NOT_ALLOWED, "not available")]
        }

        // ── Unhandled commands ──────────────────────────────────────
        //
        // TODO(golden-trace): remaining legacy commands — libraryalbumcover
        // (W3 batch 3d), rating setters, verifyconnection — are backed by W2
        // callbacks but still need wire shape pinned against fixtures.
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
///
/// Wire shape: `{"current": <ms>, "total": <ms>}`, mirroring the C#
/// `PlaybackPosition` entity. `total` is 0 for radio streams without a
/// known duration.
async fn handle_position(data: &serde_json::Value, state: &Arc<AppState>) -> Vec<SocketMessage> {
    if let Some(pos) = parse_int_from_data(data) {
        let s = Arc::clone(state);
        let _ = tokio::task::spawn_blocking(move || s.callbacks().player_set_position(pos)).await;
    }

    let s = Arc::clone(state);
    match tokio::task::spawn_blocking(move || s.callbacks().query_playback_position()).await {
        Ok(Ok(p)) => vec![SocketMessage::new(
            constants::NOW_PLAYING_POSITION,
            serde_json::json!({ "current": p.current, "total": p.total }),
        )],
        Ok(Err(e)) => {
            warn!("PlaybackPosition query failed: {}", e);
            vec![]
        }
        Err(e) => {
            warn!("spawn_blocking panicked: {}", e);
            vec![]
        }
    }
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
        messages.push(SocketMessage::new(
            constants::PLAYER_STATUS,
            legacy_player_status_payload(&ps),
        ));
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

/// Extract `{offset, limit}` from a request payload. Defaults mirror
/// what MusicBee's legacy server uses when a client omits them.
fn parse_pagination(data: &serde_json::Value) -> (i32, i32) {
    let offset = data
        .get("offset")
        .and_then(|v| v.as_i64())
        .unwrap_or(0) as i32;
    let limit = data
        .get("limit")
        .and_then(|v| v.as_i64())
        .unwrap_or(5000) as i32;
    (offset, limit)
}

/// Wrap a collection in the legacy paginated response envelope.
///
/// `total` is reported as the position of the last returned item
/// (`offset + data.len()`). The real C# server tracks the total
/// independently of the returned page; the internal DTOs currently
/// don't carry that number, so this is the best the wire can express
/// without extending the FFI. Tier A shape-diff is unaffected; a
/// future Tier B+ might want a richer DTO with explicit `total`.
fn paginated_envelope<T: serde::Serialize>(
    data: Vec<T>,
    offset: i32,
    limit: i32,
) -> serde_json::Value {
    let total = offset + data.len() as i32;
    serde_json::json!({
        "data": data,
        "offset": offset,
        "limit": limit,
        "total": total,
    })
}

/// Shared query-and-emit helper for no-params queries.
/// `transform` converts the typed callback response into the exact
/// legacy wire-format `data` payload.
async fn simple_query<T, F>(
    state: &Arc<AppState>,
    query_type: QueryType,
    response_context: &'static str,
    transform: F,
) -> Vec<SocketMessage>
where
    T: serde::de::DeserializeOwned + Send + 'static,
    F: FnOnce(T) -> serde_json::Value + Send + 'static,
{
    let s = Arc::clone(state);
    match tokio::task::spawn_blocking(move || s.callbacks().query_no_params::<T>(query_type)).await {
        Ok(Ok(resp)) => vec![SocketMessage::new(response_context, transform(resp))],
        Ok(Err(e)) => {
            warn!(context = response_context, error = %e, "query failed");
            vec![]
        }
        Err(e) => {
            warn!(context = response_context, error = %e, "spawn_blocking panicked");
            vec![]
        }
    }
}

/// Fire-and-forget dispatch for `libraryqueue{genre,artist,album,track}`.
/// Payload shape (from fixtures): `{"query": "<text>", "type": "<now|next|last|add|...>"}`.
async fn handle_library_queue(
    data: &serde_json::Value,
    state: &Arc<AppState>,
    target: LibraryQueueTarget,
) -> Vec<SocketMessage> {
    let query = data
        .get("query")
        .and_then(|v| v.as_str())
        .unwrap_or("")
        .to_owned();
    let queue_type = data
        .get("type")
        .and_then(|v| v.as_str())
        .unwrap_or("next")
        .to_owned();

    if query.is_empty() {
        return vec![];
    }

    let s = Arc::clone(state);
    let _ = tokio::task::spawn_blocking(move || s.callbacks().library_queue(target, &queue_type, &query))
        .await;
    vec![]
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
