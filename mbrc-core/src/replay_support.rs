//! Hidden test support for the golden-trace replay harness. Re-exports
//! the internals the harness needs (connection handler, `AppState`
//! constructor, mock callbacks) without leaking them into the crate's
//! documented public API.
//!
//! Not covered by semver — may change at any time.
#![doc(hidden)]

use std::ffi::c_int;
use std::ptr;

pub use crate::ffi::callbacks::SafeCallbacks;
pub use crate::ffi::types::{MbrcCallbacks, QueryType};
pub use crate::server::legacy::connection::handle_connection;
pub use crate::state::AppState;
pub use crate::server::{
    AlbumCoverResponse, AlbumDto, AlbumListResponse, ArtistDto, ArtistListResponse,
    CoverCacheBuildStatusResponse, GenreDto, GenreListResponse, NowPlayingDetailsResponse,
    NowPlayingListResponse, NowPlayingTrackDto, OutputDevicesResponse, PlaybackPositionResponse,
    PlayerStateResponse, PlaylistDto, PlaylistListResponse, RadioStationDto, RadioStationsResponse,
    TrackDto, TrackInfoResponse, TrackListResponse,
};

/// Placeholder PNG (1×1 transparent) matching the one written by
/// `mbrc-fixture-trim` into `tests/golden/_assets/placeholder-cover.png`.
/// The replay mock returns this exact base64 for any cover query, and
/// the fixture pipeline rewrites every captured cover to this same
/// string — so byte-diffs pass without needing real cover bytes.
const PLACEHOLDER_PNG_B64: &str =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNgAAIAAAUAAeIVWUMAAAAASUVORK5CYII=";

extern "C" fn nop_thin() -> c_int {
    0
}

extern "C" fn nop_thin_int(_: c_int) -> c_int {
    0
}

/// Query dispatcher with canned responses for every legacy QueryType.
/// Each response is structurally faithful to the runtime DTO (non-empty
/// collections, plausible string values) so the legacy handler produces
/// a wire-format response the replay harness can shape-diff against.
///
/// Values are deterministic and match across calls, so the seeded state
/// is effectively a snapshot of a "toy library" frozen in time. The
/// harness does shape-level diffs, so exact field values don't need to
/// match the fixture — only the envelope structure does.
extern "C" fn seeded_query(
    ty: i32,
    _buf: *const u8,
    _len: u32,
    out_buf: *mut *mut u8,
    out_len: *mut u32,
) -> i32 {
    let bytes: Option<Vec<u8>> = seed_response(ty);

    match bytes {
        Some(v) => {
            let mut boxed = v.into_boxed_slice();
            let len = boxed.len() as u32;
            let ptr = boxed.as_mut_ptr();
            std::mem::forget(boxed);
            unsafe {
                *out_buf = ptr;
                *out_len = len;
            }
            0
        }
        None => {
            unsafe {
                *out_buf = ptr::null_mut();
                *out_len = 0;
            }
            0
        }
    }
}

fn seed_response(ty: i32) -> Option<Vec<u8>> {
    if ty == QueryType::PlayerState as i32 {
        return rmp_serde::to_vec_named(&PlayerStateResponse {
            play_state: "Playing".into(),
            volume: 72,
            mute: false,
            shuffle: "shuffle".into(),
            repeat: "All".into(),
            position: 12345,
            scrobble: false,
        })
        .ok();
    }
    if ty == QueryType::TrackInfo as i32 {
        return rmp_serde::to_vec_named(&TrackInfoResponse {
            artist: "Seeded Artist".into(),
            title: "Seeded Title".into(),
            album: "Seeded Album".into(),
            year: "2026".into(),
            path: r"\\share\music\seeded\01 Track.mp3".into(),
        })
        .ok();
    }
    if ty == QueryType::CoverData as i32 {
        return rmp_serde::to_vec_named(&PLACEHOLDER_PNG_B64.to_string()).ok();
    }
    if ty == QueryType::Lyrics as i32 {
        return rmp_serde::to_vec_named(&"<placeholder lyrics>".to_string()).ok();
    }
    if ty == QueryType::PlaylistList as i32 {
        return rmp_serde::to_vec_named(&PlaylistListResponse {
            playlists: vec![PlaylistDto {
                url: "seeded.m3u".into(),
                name: "Seeded Playlist".into(),
            }],
        })
        .ok();
    }
    if ty == QueryType::NowPlayingList as i32 {
        return rmp_serde::to_vec_named(&NowPlayingListResponse {
            tracks: vec![NowPlayingTrackDto {
                artist: "Seeded Artist".into(),
                album: "Seeded Album".into(),
                album_artist: "Seeded Artist".into(),
                title: "Seeded Title".into(),
                path: r"\\share\music\seeded\01 Track.mp3".into(),
                position: 0,
            }],
        })
        .ok();
    }
    if ty == QueryType::RadioStations as i32 {
        return rmp_serde::to_vec_named(&RadioStationsResponse {
            stations: vec![RadioStationDto {
                name: "Seeded Radio".into(),
                url: "http://example.invalid/stream".into(),
            }],
        })
        .ok();
    }
    if ty == QueryType::OutputDevices as i32 {
        return rmp_serde::to_vec_named(&OutputDevicesResponse {
            active: "Primary".into(),
            devices: vec!["Primary".into(), "Secondary".into()],
        })
        .ok();
    }
    if ty == QueryType::LibrarySearchGenre as i32
        || ty == QueryType::LibraryBrowseGenres as i32
    {
        return rmp_serde::to_vec_named(&GenreListResponse {
            genres: vec![GenreDto {
                genre: "Seeded Genre".into(),
                count: 42,
            }],
        })
        .ok();
    }
    if ty == QueryType::LibrarySearchArtist as i32
        || ty == QueryType::LibraryBrowseArtists as i32
        || ty == QueryType::LibraryGenreArtists as i32
    {
        return rmp_serde::to_vec_named(&ArtistListResponse {
            artists: vec![ArtistDto {
                artist: "Seeded Artist".into(),
                count: 12,
            }],
        })
        .ok();
    }
    if ty == QueryType::LibrarySearchAlbum as i32
        || ty == QueryType::LibraryBrowseAlbums as i32
        || ty == QueryType::LibraryArtistAlbums as i32
    {
        return rmp_serde::to_vec_named(&AlbumListResponse {
            albums: vec![AlbumDto {
                artist: "Seeded Artist".into(),
                album: "Seeded Album".into(),
                count: 12,
            }],
        })
        .ok();
    }
    if ty == QueryType::LibrarySearchTitle as i32
        || ty == QueryType::LibraryBrowseTracks as i32
        || ty == QueryType::LibraryAlbumTracks as i32
    {
        return rmp_serde::to_vec_named(&TrackListResponse {
            tracks: vec![TrackDto {
                src: r"\\share\music\seeded\01 Track.mp3".into(),
                artist: "Seeded Artist".into(),
                title: "Seeded Title".into(),
                trackno: 1,
                disc: 1,
                album: "Seeded Album".into(),
                album_artist: "Seeded Artist".into(),
                genre: "Seeded Genre".into(),
            }],
        })
        .ok();
    }
    if ty == QueryType::NowPlayingDetails as i32 {
        return rmp_serde::to_vec_named(&NowPlayingDetailsResponse {
            album_artist: "Seeded Artist".into(),
            genre: "Seeded Genre".into(),
            track_no: "1".into(),
            track_count: "10".into(),
            disc_no: "1".into(),
            disc_count: "1".into(),
            publisher: "".into(),
            composer: "".into(),
            comment: "".into(),
            grouping: "".into(),
            rating_album: "".into(),
            encoder: "LAME".into(),
            kind: "mp3".into(),
            format: "MPEG-1 Layer 3".into(),
            size: "5242880".into(),
            channels: "2".into(),
            sample_rate: "44100".into(),
            bitrate: "320".into(),
            date_modified: "2026-01-01".into(),
            date_added: "2026-01-01".into(),
            last_played: "2026-04-01".into(),
            play_count: "5".into(),
            skip_count: "0".into(),
            duration: "180000".into(),
        })
        .ok();
    }
    if ty == QueryType::AlbumCover as i32 {
        return rmp_serde::to_vec_named(&AlbumCoverResponse {
            album: "Seeded Album".into(),
            artist: "Seeded Artist".into(),
            cover: PLACEHOLDER_PNG_B64.into(),
            status: 200,
            hash: "seeded-hash".into(),
        })
        .ok();
    }
    if ty == QueryType::CoverCacheBuildStatus as i32 {
        return rmp_serde::to_vec_named(&CoverCacheBuildStatusResponse { building: false })
            .ok();
    }
    if ty == QueryType::PlaybackPosition as i32 {
        return rmp_serde::to_vec_named(&PlaybackPositionResponse {
            current: 12345,
            total: 192136,
        })
        .ok();
    }
    None
}

extern "C" fn nop_fat(
    _ty: i32,
    _buf: *const u8,
    _len: u32,
    out_buf: *mut *mut u8,
    out_len: *mut u32,
) -> i32 {
    unsafe {
        *out_buf = ptr::null_mut();
        *out_len = 0;
    }
    0
}

/// Free memory that was allocated via `Box::into_raw` in `seeded_query`.
extern "C" fn free_buffer(buf: *mut u8) {
    if !buf.is_null() {
        // SAFETY: pointer came from Box::into_raw of a slice originally.
        // We don't know the exact length here, but Rust's allocator for
        // Box<[u8]> tracks it via the slice's fat pointer — which means
        // we can't safely free with just a thin pointer. The legitimate
        // solution in the FFI is that the host calls free_buffer with
        // the original allocation; for the test, leaking is tolerable.
        // Production callbacks will provide a real free.
        let _ = buf;
    }
}

/// Seeded callbacks for golden-trace replay. Thin ops succeed silently;
/// query dispatcher returns canned `PlayerStateResponse` for now, empty
/// for everything else (those handlers stay silent until seeded).
pub fn nop_callbacks() -> SafeCallbacks {
    SafeCallbacks::new(MbrcCallbacks {
        player_play_pause: Some(nop_thin),
        player_stop: Some(nop_thin),
        player_next: Some(nop_thin),
        player_previous: Some(nop_thin),
        player_set_volume: Some(nop_thin_int),
        player_set_position: Some(nop_thin_int),
        query_data: Some(seeded_query),
        execute_command: Some(nop_fat),
        free_buffer: Some(free_buffer),
    })
}
