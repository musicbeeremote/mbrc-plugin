//! DTO schema drift guard.
//!
//! Each canonical JSON under `tests/schemas/` is the contract the C# side
//! must also understand. If a serde `rename`, field-name typo, or deletion
//! lands on the Rust side, round-tripping the canonical JSON loses data and
//! the matching test here fails. The C# companion tests assert the same
//! round-trip on the C# DTOs. One side drifting → CI red.

use mbrc_core::{
    AlbumCoverParams, AlbumCoverResponse, AlbumListResponse, ArtistListResponse, BrowseParams,
    CoverCacheBuildStatusResponse, GenreListResponse, IndexParams, LibraryQueueParams, MoveParams,
    NowPlayingDetailsResponse, NowPlayingListResponse, OutputDevicesResponse, PaginationParams,
    PlayerStateResponse, PlaylistListResponse, QueryParams, RadioStationsResponse, SetBoolParams,
    SetLfmRatingParams, SetRepeatParams, StringValueParams, TrackInfoResponse, TrackListResponse,
};
use serde_json::Value;

fn assert_roundtrip<T>(path: &str)
where
    T: serde::de::DeserializeOwned + serde::Serialize,
{
    let canonical =
        std::fs::read_to_string(path).unwrap_or_else(|e| panic!("read {}: {}", path, e));
    let expected: Value = serde_json::from_str(&canonical)
        .unwrap_or_else(|e| panic!("parse canonical {}: {}", path, e));

    let dto: T = serde_json::from_value(expected.clone())
        .unwrap_or_else(|e| panic!("deserialize {} into DTO: {}", path, e));
    let actual: Value =
        serde_json::to_value(&dto).unwrap_or_else(|e| panic!("re-serialize {}: {}", path, e));

    assert_eq!(
        actual, expected,
        "round-trip drift for {}: field name or shape mismatch",
        path
    );
}

#[test]
fn player_state_roundtrips() {
    assert_roundtrip::<PlayerStateResponse>("tests/schemas/player_state.json");
}

#[test]
fn track_info_roundtrips() {
    assert_roundtrip::<TrackInfoResponse>("tests/schemas/track_info.json");
}

#[test]
fn set_bool_params_roundtrips() {
    assert_roundtrip::<SetBoolParams>("tests/schemas/set_bool_params.json");
}

#[test]
fn set_repeat_params_roundtrips() {
    assert_roundtrip::<SetRepeatParams>("tests/schemas/set_repeat_params.json");
}

#[test]
fn string_value_params_roundtrips() {
    assert_roundtrip::<StringValueParams>("tests/schemas/string_value_params.json");
}

#[test]
fn set_lfm_rating_params_roundtrips() {
    assert_roundtrip::<SetLfmRatingParams>("tests/schemas/set_lfm_rating_params.json");
}

#[test]
fn index_params_roundtrips() {
    assert_roundtrip::<IndexParams>("tests/schemas/index_params.json");
}

#[test]
fn move_params_roundtrips() {
    assert_roundtrip::<MoveParams>("tests/schemas/move_params.json");
}

#[test]
fn library_queue_params_roundtrips() {
    assert_roundtrip::<LibraryQueueParams>("tests/schemas/library_queue_params.json");
}

#[test]
fn pagination_params_roundtrips() {
    assert_roundtrip::<PaginationParams>("tests/schemas/pagination_params.json");
}

#[test]
fn playlist_list_response_roundtrips() {
    assert_roundtrip::<PlaylistListResponse>("tests/schemas/playlist_list_response.json");
}

#[test]
fn now_playing_list_response_roundtrips() {
    assert_roundtrip::<NowPlayingListResponse>("tests/schemas/now_playing_list_response.json");
}

#[test]
fn radio_stations_response_roundtrips() {
    assert_roundtrip::<RadioStationsResponse>("tests/schemas/radio_stations_response.json");
}

#[test]
fn output_devices_response_roundtrips() {
    assert_roundtrip::<OutputDevicesResponse>("tests/schemas/output_devices_response.json");
}

#[test]
fn query_params_roundtrips() {
    assert_roundtrip::<QueryParams>("tests/schemas/query_params.json");
}

#[test]
fn browse_params_roundtrips() {
    assert_roundtrip::<BrowseParams>("tests/schemas/browse_params.json");
}

#[test]
fn album_cover_params_roundtrips() {
    assert_roundtrip::<AlbumCoverParams>("tests/schemas/album_cover_params.json");
}

#[test]
fn genre_list_response_roundtrips() {
    assert_roundtrip::<GenreListResponse>("tests/schemas/genre_list_response.json");
}

#[test]
fn artist_list_response_roundtrips() {
    assert_roundtrip::<ArtistListResponse>("tests/schemas/artist_list_response.json");
}

#[test]
fn album_list_response_roundtrips() {
    assert_roundtrip::<AlbumListResponse>("tests/schemas/album_list_response.json");
}

#[test]
fn track_list_response_roundtrips() {
    assert_roundtrip::<TrackListResponse>("tests/schemas/track_list_response.json");
}

#[test]
fn now_playing_details_response_roundtrips() {
    assert_roundtrip::<NowPlayingDetailsResponse>(
        "tests/schemas/now_playing_details_response.json",
    );
}

#[test]
fn album_cover_response_roundtrips() {
    assert_roundtrip::<AlbumCoverResponse>("tests/schemas/album_cover_response.json");
}

#[test]
fn cover_cache_build_status_response_roundtrips() {
    assert_roundtrip::<CoverCacheBuildStatusResponse>(
        "tests/schemas/cover_cache_build_status_response.json",
    );
}
