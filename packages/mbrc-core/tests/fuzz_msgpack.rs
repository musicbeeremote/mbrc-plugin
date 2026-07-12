//! Property-based fuzzing of the MessagePack decode boundary. Every FFI result
//! type (a query reply the C# host returns) and the param DTOs are decoded from
//! arbitrary bytes: malformed / truncated / type-confused MessagePack must always
//! be an `Err`, never a panic, so a buggy or hostile host can't crash the core.
//!
//! cargo-fuzz would be the coverage-guided tool but needs nightly; the toolchain
//! is pinned to stable, so this runs as proptest cases under `cargo test` + CI.

use mbrc_core::ffi::dtos::*;
use mbrc_core::protocol::messages::*;
use proptest::prelude::*;

/// Decode `bytes` as each listed type, discarding the result. The only assertion
/// is the absence of a panic (a control-flow property, not a value one).
macro_rules! try_decode {
    ($bytes:expr, $($t:ty),+ $(,)?) => {
        $( let _ = rmp_serde::from_slice::<$t>($bytes); )+
    };
}

proptest! {
    // Query-result DTOs (C# -> core) decoded from arbitrary bytes never panic.
    #[test]
    fn result_dtos_decode_never_panic(bytes in prop::collection::vec(any::<u8>(), 0..2048)) {
        try_decode!(&bytes,
            PlaybackPositionResponse, PlayerState, OutputDevices, TrackInfo, TrackDetails,
            Cover, Lyrics, NowPlayingListTrack, GenreData, ArtistData, AlbumData, Track,
            AlbumCover, AlbumIdentifier, TrackMetadata, AlbumCoverItem, Playlist, RadioStation,
            Page<Track>, Page<NowPlayingListTrack>, Page<Playlist>,
        );
    }

    // Param DTOs (the shapes crossing the boundary) decoded from arbitrary bytes
    // never panic either.
    #[test]
    fn param_dtos_decode_never_panic(bytes in prop::collection::vec(any::<u8>(), 0..2048)) {
        try_decode!(&bytes,
            SetBoolParams, SetRepeatParams, StringValueParams, SetLfmRatingParams, IndexParams,
            SetIntParams, MoveParams, PaginationParams, QueryParams, BrowseParams,
            NowPlayingQueueParams, TagChangeParams, AlbumCoverParams, PathParams, BatchMetadataParams,
        );
    }

    // Type confusion: a *valid* named-map msgpack encoding (the shape every DTO
    // decodes from) with arbitrary keys/values, decoded as each DTO, must be a
    // clean Err/Ok - never a panic. Mixes int and string values so both matching
    // and mismatching field types are exercised.
    #[test]
    fn cross_type_decode_never_panics(
        ints in prop::collection::btree_map("[a-z_]{1,12}", any::<i64>(), 0..6),
        strs in prop::collection::btree_map("[a-z_]{1,12}", ".*", 0..6),
    ) {
        // Encode a serde_json object (a named map) as MessagePack, then try to
        // decode it as unrelated DTOs.
        let mut obj = serde_json::Map::new();
        for (k, v) in ints {
            obj.insert(k, serde_json::Value::from(v));
        }
        for (k, v) in strs {
            obj.insert(k, serde_json::Value::from(v));
        }
        let bytes = rmp_serde::to_vec_named(&serde_json::Value::Object(obj)).expect("encode map");
        try_decode!(&bytes,
            PlayerState, TrackInfo, Cover, Lyrics, Page<Track>, PaginationParams, MoveParams,
        );
    }
}
