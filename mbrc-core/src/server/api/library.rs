//! `/api/v1/library` — library browse, search, drill-down, queue.
//!
//! Browse and search responses share a `Page<T>` envelope with
//! `{items, offset, limit, total}`. Drill-down queries (artist→albums,
//! album→tracks, genre→artists) reuse the same envelope with
//! `offset=0, limit=total=items.len()` so clients see one shape.
//!
//! Pagination is taken from `?offset=&limit=` query string; defaults
//! mirror the legacy server (`offset=0`, `limit=5000`).
//!
//! - `GET /genres`                                      browse all
//! - `GET /artists?album_artists=bool`                  browse all
//! - `GET /albums`, `GET /tracks`                       browse all
//! - `GET /search/{genres|artists|albums|tracks}?q=`    search
//! - `GET /genres/{name}/artists`                       drill-down
//! - `GET /artists/{name}/albums`
//! - `GET /albums/{name}/tracks`
//! - `POST /play_all` body `{shuffle: bool}`
//! - `POST /queue`    body `{target, queue_type, query}`

use std::sync::Arc;

use axum::extract::{Path, Query, State};
use axum::http::StatusCode;
use axum::routing::{get, post};
use axum::{Json, Router};
use serde::{Deserialize, Serialize};
use tracing::warn;

use crate::ffi::callbacks::LibraryQueueTarget;
use crate::server::{AlbumDto, ArtistDto, GenreDto, TrackDto};
use crate::state::AppState;

use super::error::{ApiError, ApiResult};

pub fn routes() -> Router<Arc<AppState>> {
    Router::new()
        .route("/genres", get(browse_genres))
        .route("/artists", get(browse_artists))
        .route("/albums", get(browse_albums))
        .route("/tracks", get(browse_tracks))
        .route("/search/genres", get(search_genres))
        .route("/search/artists", get(search_artists))
        .route("/search/albums", get(search_albums))
        .route("/search/tracks", get(search_tracks))
        .route("/genres/:name/artists", get(genre_artists))
        .route("/artists/:name/albums", get(artist_albums))
        .route("/albums/:name/tracks", get(album_tracks))
        .route("/play_all", post(post_play_all))
        .route("/queue", post(post_queue))
}

// ── Helpers ────────────────────────────────────────────────────────

#[derive(Deserialize)]
struct Pagination {
    #[serde(default)]
    offset: Option<i32>,
    #[serde(default)]
    limit: Option<i32>,
    #[serde(default)]
    album_artists: Option<bool>,
}

impl Pagination {
    fn resolve(&self) -> (i32, i32) {
        (self.offset.unwrap_or(0), self.limit.unwrap_or(5000))
    }
}

#[derive(Deserialize)]
struct SearchQuery {
    #[serde(default)]
    q: String,
}

#[derive(Serialize)]
struct Page<T: Serialize> {
    items: Vec<T>,
    offset: i32,
    limit: i32,
    total: i32,
}

impl<T: Serialize> Page<T> {
    fn paginated(items: Vec<T>, offset: i32, limit: i32) -> Self {
        let total = offset + items.len() as i32;
        Self {
            items,
            offset,
            limit,
            total,
        }
    }

    fn unpaginated(items: Vec<T>) -> Self {
        let total = items.len() as i32;
        Self {
            items,
            offset: 0,
            limit: total,
            total,
        }
    }
}

async fn run_query<R, F>(label: &'static str, f: F) -> ApiResult<R>
where
    R: Send + 'static,
    F: FnOnce() -> Result<R, String> + Send + 'static,
{
    tokio::task::spawn_blocking(f)
        .await
        .map_err(|e| {
            warn!("{} spawn_blocking panicked: {}", label, e);
            ApiError::internal(format!("{} query panicked", label))
        })?
        .map_err(|e| {
            warn!("{} query failed: {}", label, e);
            ApiError::internal(format!("{} query failed", label))
        })
}

// ── Browse ─────────────────────────────────────────────────────────

async fn browse_genres(
    State(state): State<Arc<AppState>>,
    Query(p): Query<Pagination>,
) -> ApiResult<Json<Page<GenreDto>>> {
    let (offset, limit) = p.resolve();
    let r = run_query("LibraryBrowseGenres", move || {
        state.callbacks().library_browse_genres(offset, limit)
    })
    .await?;
    Ok(Json(Page::paginated(r.genres, offset, limit)))
}

async fn browse_artists(
    State(state): State<Arc<AppState>>,
    Query(p): Query<Pagination>,
) -> ApiResult<Json<Page<ArtistDto>>> {
    let (offset, limit) = p.resolve();
    let album_artists = p.album_artists.unwrap_or(false);
    let r = run_query("LibraryBrowseArtists", move || {
        state
            .callbacks()
            .library_browse_artists(offset, limit, album_artists)
    })
    .await?;
    Ok(Json(Page::paginated(r.artists, offset, limit)))
}

async fn browse_albums(
    State(state): State<Arc<AppState>>,
    Query(p): Query<Pagination>,
) -> ApiResult<Json<Page<AlbumDto>>> {
    let (offset, limit) = p.resolve();
    let r = run_query("LibraryBrowseAlbums", move || {
        state.callbacks().library_browse_albums(offset, limit)
    })
    .await?;
    Ok(Json(Page::paginated(r.albums, offset, limit)))
}

async fn browse_tracks(
    State(state): State<Arc<AppState>>,
    Query(p): Query<Pagination>,
) -> ApiResult<Json<Page<TrackDto>>> {
    let (offset, limit) = p.resolve();
    let r = run_query("LibraryBrowseTracks", move || {
        state.callbacks().library_browse_tracks(offset, limit)
    })
    .await?;
    Ok(Json(Page::paginated(r.tracks, offset, limit)))
}

// ── Search ─────────────────────────────────────────────────────────

async fn search_genres(
    State(state): State<Arc<AppState>>,
    Query(s): Query<SearchQuery>,
) -> ApiResult<Json<Page<GenreDto>>> {
    let q = s.q;
    let r = run_query("LibrarySearchGenre", move || {
        state.callbacks().library_search_genres(&q)
    })
    .await?;
    Ok(Json(Page::unpaginated(r.genres)))
}

async fn search_artists(
    State(state): State<Arc<AppState>>,
    Query(s): Query<SearchQuery>,
) -> ApiResult<Json<Page<ArtistDto>>> {
    let q = s.q;
    let r = run_query("LibrarySearchArtist", move || {
        state.callbacks().library_search_artists(&q)
    })
    .await?;
    Ok(Json(Page::unpaginated(r.artists)))
}

async fn search_albums(
    State(state): State<Arc<AppState>>,
    Query(s): Query<SearchQuery>,
) -> ApiResult<Json<Page<AlbumDto>>> {
    let q = s.q;
    let r = run_query("LibrarySearchAlbum", move || {
        state.callbacks().library_search_albums(&q)
    })
    .await?;
    Ok(Json(Page::unpaginated(r.albums)))
}

async fn search_tracks(
    State(state): State<Arc<AppState>>,
    Query(s): Query<SearchQuery>,
) -> ApiResult<Json<Page<TrackDto>>> {
    let q = s.q;
    let r = run_query("LibrarySearchTitle", move || {
        state.callbacks().library_search_titles(&q)
    })
    .await?;
    Ok(Json(Page::unpaginated(r.tracks)))
}

// ── Drill-down ─────────────────────────────────────────────────────

async fn genre_artists(
    State(state): State<Arc<AppState>>,
    Path(name): Path<String>,
) -> ApiResult<Json<Page<ArtistDto>>> {
    let r = run_query("LibraryGenreArtists", move || {
        state.callbacks().library_genre_artists(&name)
    })
    .await?;
    Ok(Json(Page::unpaginated(r.artists)))
}

async fn artist_albums(
    State(state): State<Arc<AppState>>,
    Path(name): Path<String>,
) -> ApiResult<Json<Page<AlbumDto>>> {
    let r = run_query("LibraryArtistAlbums", move || {
        state.callbacks().library_artist_albums(&name)
    })
    .await?;
    Ok(Json(Page::unpaginated(r.albums)))
}

async fn album_tracks(
    State(state): State<Arc<AppState>>,
    Path(name): Path<String>,
) -> ApiResult<Json<Page<TrackDto>>> {
    let r = run_query("LibraryAlbumTracks", move || {
        state.callbacks().library_album_tracks(&name)
    })
    .await?;
    Ok(Json(Page::unpaginated(r.tracks)))
}

// ── Commands ───────────────────────────────────────────────────────

#[derive(Deserialize)]
struct PlayAllBody {
    #[serde(default)]
    shuffle: bool,
}

async fn post_play_all(
    State(state): State<Arc<AppState>>,
    Json(body): Json<PlayAllBody>,
) -> StatusCode {
    let shuffle = body.shuffle;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().library_play_all(shuffle);
    })
    .await
    .ok();
    StatusCode::NO_CONTENT
}

#[derive(Deserialize)]
struct QueueBody {
    target: String,
    #[serde(default)]
    queue_type: String,
    #[serde(default)]
    query: String,
}

async fn post_queue(
    State(state): State<Arc<AppState>>,
    Json(body): Json<QueueBody>,
) -> ApiResult<StatusCode> {
    let target = match body.target.as_str() {
        "genre" => LibraryQueueTarget::Genre,
        "artist" => LibraryQueueTarget::Artist,
        "album" => LibraryQueueTarget::Album,
        "track" => LibraryQueueTarget::Track,
        other => {
            return Err(ApiError::bad_request(format!(
                "unknown queue target {:?} (expected genre|artist|album|track)",
                other
            )));
        }
    };
    let queue_type = body.queue_type;
    let query = body.query;
    tokio::task::spawn_blocking(move || {
        let _ = state.callbacks().library_queue(target, &queue_type, &query);
    })
    .await
    .ok();
    Ok(StatusCode::NO_CONTENT)
}
