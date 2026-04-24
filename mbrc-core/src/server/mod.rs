pub mod legacy;

use std::sync::Arc;

use axum::extract::State;
use axum::routing::get;
use axum::{Json, Router};
use hyper::service::service_fn;
use hyper_util::rt::{TokioExecutor, TokioIo};
use hyper_util::server::conn::auto;
use serde::{Deserialize, Serialize};
use tokio::net::{TcpListener, TcpStream};
use tokio::sync::oneshot;
use tower::Service;
use tracing::{debug, error, info, warn};

use crate::discovery;
use crate::ffi::types::QueryType;
use crate::state::AppState;

/// Player state returned by the `QueryType::PlayerState` callback.
#[derive(Debug, Serialize, Deserialize)]
pub struct PlayerStateResponse {
    #[serde(default)]
    pub play_state: String,
    #[serde(default)]
    pub volume: i32,
    #[serde(default)]
    pub mute: bool,
    #[serde(default)]
    pub shuffle: String,
    #[serde(default)]
    pub repeat: String,
    #[serde(default)]
    pub position: i32,
    #[serde(default)]
    pub scrobble: bool,
}

/// Track info returned by the `QueryType::TrackInfo` callback.
#[derive(Debug, Serialize, Deserialize)]
pub struct TrackInfoResponse {
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub album: String,
    #[serde(default)]
    pub year: String,
    #[serde(default)]
    pub path: String,
}

/// One entry in a `PlaylistList` response.
#[derive(Debug, Serialize, Deserialize)]
pub struct PlaylistDto {
    #[serde(default)]
    pub url: String,
    #[serde(default)]
    pub name: String,
}

/// Response payload for `QueryType::PlaylistList`.
#[derive(Debug, Serialize, Deserialize)]
pub struct PlaylistListResponse {
    #[serde(default)]
    pub playlists: Vec<PlaylistDto>,
}

/// One track in a paginated `NowPlayingList` response.
/// Field names mirror the legacy `NowPlaying` entity on the C# side.
#[derive(Debug, Serialize, Deserialize)]
pub struct NowPlayingTrackDto {
    #[serde(default)]
    pub artist: String,
    // `album` and `album_artist` are omitted when empty to mirror the
    // C# `NullValueHandling.Ignore` global setting — MusicBee returns
    // null for tracks whose tags aren't populated, and the legacy wire
    // strips those keys entirely rather than emitting `""`.
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub album: String,
    #[serde(default, skip_serializing_if = "String::is_empty")]
    pub album_artist: String,
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub path: String,
    #[serde(default)]
    pub position: i32,
}

/// Response payload for `QueryType::NowPlayingList`.
#[derive(Debug, Serialize, Deserialize)]
pub struct NowPlayingListResponse {
    #[serde(default)]
    pub tracks: Vec<NowPlayingTrackDto>,
}

/// One station in a `RadioStations` response.
#[derive(Debug, Serialize, Deserialize)]
pub struct RadioStationDto {
    #[serde(default)]
    pub name: String,
    #[serde(default)]
    pub url: String,
}

/// Response payload for `QueryType::RadioStations`.
#[derive(Debug, Serialize, Deserialize)]
pub struct RadioStationsResponse {
    #[serde(default)]
    pub stations: Vec<RadioStationDto>,
}

/// Response payload for `QueryType::OutputDevices`. `active` is the
/// currently selected device name; `devices` lists every available
/// device, in the order MusicBee reports them.
#[derive(Debug, Serialize, Deserialize)]
pub struct OutputDevicesResponse {
    #[serde(default)]
    pub active: String,
    #[serde(default)]
    pub devices: Vec<String>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct GenreDto {
    #[serde(default)]
    pub genre: String,
    #[serde(default)]
    pub count: i32,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct ArtistDto {
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub count: i32,
}

/// Library album entry. `count` is the number of tracks MusicBee
/// reports for the album, matching the legacy `AlbumData.TrackCount`.
#[derive(Debug, Serialize, Deserialize)]
pub struct AlbumDto {
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub album: String,
    #[serde(default)]
    pub count: i32,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct TrackDto {
    #[serde(default)]
    pub src: String,
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub title: String,
    #[serde(default)]
    pub trackno: i32,
    #[serde(default)]
    pub disc: i32,
    #[serde(default)]
    pub album: String,
    #[serde(default)]
    pub album_artist: String,
    #[serde(default)]
    pub genre: String,
}

/// Response payload for `LibrarySearchGenre` and `LibraryBrowseGenres`.
#[derive(Debug, Serialize, Deserialize)]
pub struct GenreListResponse {
    #[serde(default)]
    pub genres: Vec<GenreDto>,
}

/// Response payload for `LibrarySearchArtist`, `LibraryBrowseArtists`
/// and `LibraryGenreArtists`.
#[derive(Debug, Serialize, Deserialize)]
pub struct ArtistListResponse {
    #[serde(default)]
    pub artists: Vec<ArtistDto>,
}

/// Response payload for `LibrarySearchAlbum`, `LibraryBrowseAlbums`
/// and `LibraryArtistAlbums`.
#[derive(Debug, Serialize, Deserialize)]
pub struct AlbumListResponse {
    #[serde(default)]
    pub albums: Vec<AlbumDto>,
}

/// Response payload for `LibrarySearchTitle`, `LibraryBrowseTracks`
/// and `LibraryAlbumTracks`.
#[derive(Debug, Serialize, Deserialize)]
pub struct TrackListResponse {
    #[serde(default)]
    pub tracks: Vec<TrackDto>,
}

/// Response payload for `QueryType::NowPlayingDetails`. Field names
/// mirror the legacy camelCase `NowPlayingDetails` entity on the C#
/// side. Everything is a string because MusicBee exposes the raw tag
/// representations — a track with no year reads as `""`, not `null`.
#[derive(Debug, Serialize, Deserialize)]
pub struct NowPlayingDetailsResponse {
    #[serde(default, rename = "albumArtist")]
    pub album_artist: String,
    #[serde(default)]
    pub genre: String,
    #[serde(default, rename = "trackNo")]
    pub track_no: String,
    #[serde(default, rename = "trackCount")]
    pub track_count: String,
    #[serde(default, rename = "discNo")]
    pub disc_no: String,
    #[serde(default, rename = "discCount")]
    pub disc_count: String,
    #[serde(default)]
    pub publisher: String,
    #[serde(default)]
    pub composer: String,
    #[serde(default)]
    pub comment: String,
    #[serde(default)]
    pub grouping: String,
    #[serde(default, rename = "ratingAlbum")]
    pub rating_album: String,
    #[serde(default)]
    pub encoder: String,
    #[serde(default)]
    pub kind: String,
    #[serde(default)]
    pub format: String,
    #[serde(default)]
    pub size: String,
    #[serde(default)]
    pub channels: String,
    #[serde(default, rename = "sampleRate")]
    pub sample_rate: String,
    #[serde(default)]
    pub bitrate: String,
    #[serde(default, rename = "dateModified")]
    pub date_modified: String,
    #[serde(default, rename = "dateAdded")]
    pub date_added: String,
    #[serde(default, rename = "lastPlayed")]
    pub last_played: String,
    #[serde(default, rename = "playCount")]
    pub play_count: String,
    #[serde(default, rename = "skipCount")]
    pub skip_count: String,
    #[serde(default)]
    pub duration: String,
}

/// Response payload for `QueryType::AlbumCover`. Matches the legacy
/// `AlbumCoverPayload` shape (status is HTTP-style: 200/304/400/404).
#[derive(Debug, Serialize, Deserialize)]
pub struct AlbumCoverResponse {
    #[serde(default)]
    pub album: String,
    #[serde(default)]
    pub artist: String,
    #[serde(default)]
    pub cover: String,
    #[serde(default)]
    pub status: i32,
    #[serde(default)]
    pub hash: String,
}

/// Response payload for `QueryType::CoverCacheBuildStatus`. `building`
/// is true while the C# `CoverService` is indexing album artwork.
#[derive(Debug, Serialize, Deserialize)]
pub struct CoverCacheBuildStatusResponse {
    #[serde(default)]
    pub building: bool,
}

/// Response payload for `QueryType::PlaybackPosition`. Mirrors the C#
/// `PlaybackPosition` entity (`current` / `total` in milliseconds).
/// `total` is 0 for radio streams with unknown duration.
#[derive(Debug, Serialize, Deserialize)]
pub struct PlaybackPositionResponse {
    #[serde(default)]
    pub current: i32,
    #[serde(default)]
    pub total: i32,
}

#[derive(Serialize)]
struct HealthResponse {
    status: &'static str,
    version: &'static str,
}

#[derive(Serialize)]
struct DebugPlayerResponse {
    player: Option<PlayerStateResponse>,
    track: Option<TrackInfoResponse>,
    #[serde(skip_serializing_if = "Option::is_none")]
    error: Option<String>,
}

async fn health_handler() -> Json<HealthResponse> {
    Json(HealthResponse {
        status: "ok",
        version: env!("CARGO_PKG_VERSION"),
    })
}

async fn debug_player_handler(State(state): State<Arc<AppState>>) -> Json<DebugPlayerResponse> {
    let player = match tokio::task::spawn_blocking({
        let state = Arc::clone(&state);
        move || {
            state
                .callbacks()
                .query_no_params::<PlayerStateResponse>(QueryType::PlayerState)
        }
    })
    .await
    {
        Ok(Ok(p)) => Some(p),
        Ok(Err(e)) => {
            error!("Player state query failed: {}", e);
            return Json(DebugPlayerResponse {
                player: None,
                track: None,
                error: Some(format!("Player state query failed: {}", e)),
            });
        }
        Err(e) => {
            error!("spawn_blocking panicked: {}", e);
            return Json(DebugPlayerResponse {
                player: None,
                track: None,
                error: Some(format!("Internal error: {}", e)),
            });
        }
    };

    let track = match tokio::task::spawn_blocking({
        let state = Arc::clone(&state);
        move || {
            state
                .callbacks()
                .query_no_params::<TrackInfoResponse>(QueryType::TrackInfo)
        }
    })
    .await
    {
        Ok(Ok(t)) => Some(t),
        Ok(Err(e)) => {
            error!("Track info query failed: {}", e);
            None
        }
        Err(e) => {
            error!("spawn_blocking panicked: {}", e);
            None
        }
    };

    Json(DebugPlayerResponse {
        player,
        track,
        error: None,
    })
}

fn build_router(state: Arc<AppState>) -> Router {
    Router::new()
        .route("/health", get(health_handler))
        .route("/debug/player", get(debug_player_handler))
        .with_state(state)
}

/// How the first peeked bytes classify an incoming connection.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
enum Protocol {
    Legacy,
    Http,
    Unknown,
}

/// Classify a connection by the first few peeked bytes.
///
/// - `{` — legacy JSON message (every command/response starts with an object).
/// - HTTP method token followed by space — HTTP/1.x request line.
///
/// Everything else is treated as unknown and the connection is dropped.
fn classify(peek: &[u8]) -> Protocol {
    if peek.is_empty() {
        return Protocol::Unknown;
    }
    if peek[0] == b'{' {
        return Protocol::Legacy;
    }
    // An HTTP request line is "METHOD SP ..." — match the five verbs we serve.
    const METHODS: &[&[u8]] = &[b"GET ", b"POST ", b"PUT ", b"DELETE ", b"OPTIONS "];
    for m in METHODS {
        if peek.len() >= m.len() && &peek[..m.len()] == *m {
            return Protocol::Http;
        }
    }
    Protocol::Unknown
}

/// Peek the first bytes of a connection and hand it to the matching handler.
async fn dispatch_connection(stream: TcpStream, state: Arc<AppState>, router: Router) {
    let peer = match stream.peer_addr() {
        Ok(a) => a,
        Err(e) => {
            warn!("Failed to read peer address: {}", e);
            return;
        }
    };

    // Peek does not consume from the socket, so whichever handler we pick
    // sees the full byte stream including these bytes.
    let mut probe = [0u8; 8];
    let n = match stream.peek(&mut probe).await {
        Ok(n) => n,
        Err(e) => {
            debug!(peer = %peer, "Peek failed, dropping connection: {}", e);
            return;
        }
    };

    match classify(&probe[..n]) {
        Protocol::Legacy => {
            info!(peer = %peer, "Dispatching to legacy JSON handler");
            legacy::connection::handle_connection(stream, peer, state).await;
        }
        Protocol::Http => {
            debug!(peer = %peer, "Dispatching to HTTP handler");
            let io = TokioIo::new(stream);
            let svc = router;
            let hyper_svc = service_fn(move |req| {
                let mut cloned = svc.clone();
                async move { cloned.call(req).await }
            });
            if let Err(e) = auto::Builder::new(TokioExecutor::new())
                .serve_connection_with_upgrades(io, hyper_svc)
                .await
            {
                debug!(peer = %peer, "HTTP connection ended: {}", e);
            }
        }
        Protocol::Unknown => {
            warn!(peer = %peer, probe = ?&probe[..n], "Unrecognized protocol on connection");
        }
    }
}

/// Run the single-port hybrid server. Accepts on one listener and dispatches
/// each connection to legacy or HTTP based on the first peeked bytes.
///
/// Returns when `shutdown_rx` resolves. In-flight connections are dropped.
pub async fn run(
    state: Arc<AppState>,
    port: u16,
    shutdown_rx: oneshot::Receiver<()>,
) -> Result<(), Box<dyn std::error::Error + Send + Sync>> {
    let addr = std::net::SocketAddr::from(([0, 0, 0, 0], port));
    let listener = TcpListener::bind(addr).await?;
    let router = build_router(Arc::clone(&state));

    info!(
        "Hybrid server listening on {} (legacy JSON + HTTP/WS)",
        addr
    );

    let (discovery_shutdown_tx, discovery_shutdown_rx) = oneshot::channel::<()>();
    let discovery_handle = tokio::spawn(async move {
        if let Err(e) = discovery::run(port, discovery_shutdown_rx).await {
            warn!("Discovery service error: {}", e);
        }
    });

    let mut shutdown_rx = shutdown_rx;
    loop {
        tokio::select! {
            result = listener.accept() => {
                match result {
                    Ok((stream, _)) => {
                        let state = Arc::clone(&state);
                        let router = router.clone();
                        tokio::spawn(async move {
                            dispatch_connection(stream, state, router).await;
                        });
                    }
                    Err(e) => {
                        warn!("Failed to accept connection: {}", e);
                    }
                }
            }
            _ = &mut shutdown_rx => {
                info!("Hybrid server shutting down");
                break;
            }
        }
    }

    let _ = discovery_shutdown_tx.send(());
    let _ = discovery_handle.await;

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::{classify, Protocol};

    #[test]
    fn classifies_legacy_json() {
        assert_eq!(classify(b"{\"cont"), Protocol::Legacy);
    }

    #[test]
    fn classifies_http_methods() {
        assert_eq!(classify(b"GET /api"), Protocol::Http);
        assert_eq!(classify(b"POST /x"), Protocol::Http);
        assert_eq!(classify(b"PUT /x"), Protocol::Http);
        assert_eq!(classify(b"DELETE /"), Protocol::Http);
        assert_eq!(classify(b"OPTIONS "), Protocol::Http);
    }

    #[test]
    fn rejects_unknown() {
        assert_eq!(classify(b""), Protocol::Unknown);
        assert_eq!(classify(b"HEAD /x"), Protocol::Unknown);
        assert_eq!(classify(b"\x00\x01\x02"), Protocol::Unknown);
    }
}
