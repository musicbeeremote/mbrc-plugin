use std::ffi::c_int;

use serde::de::DeserializeOwned;
use serde::Serialize;
use tracing::warn;

use crate::ffi::dtos::{
    AlbumCoverParams, BrowseParams, IndexParams, LibraryQueueParams, MoveParams,
    NowPlayingQueueParams, PaginationParams, QueryParams, SetBoolParams, SetLfmRatingParams,
    SetRepeatParams, StringValueParams,
};
use crate::ffi::types::{CommandType, MbrcCallbacks, QueryType};
use crate::server::{
    AlbumCoverBatchResponse, AlbumCoverResponse, AlbumListResponse, ArtistListResponse,
    CoverCacheBuildStatusResponse,
    GenreListResponse, NowPlayingDetailsResponse, NowPlayingListResponse, OutputDevicesResponse,
    PlaybackPositionResponse, PlaylistListResponse, RadioStationsResponse, TrackListResponse,
};

/// Safe wrapper around raw C function pointers in `MbrcCallbacks`.
///
/// Provides null-checked invocation for thin callbacks and
/// MessagePack serialization/deserialization for fat callbacks.
/// All methods are safe to call from any thread.
pub struct SafeCallbacks {
    raw: MbrcCallbacks,
}

// The raw callbacks are Send+Sync (function pointers are just addresses),
// so SafeCallbacks is too.
unsafe impl Send for SafeCallbacks {}
unsafe impl Sync for SafeCallbacks {}

#[allow(dead_code)]
impl SafeCallbacks {
    pub fn new(raw: MbrcCallbacks) -> Self {
        Self { raw }
    }

    // ── Thin callbacks ───────────────────────────────────────────────

    pub fn player_play_pause(&self) -> Result<(), &'static str> {
        match self.raw.player_play_pause {
            Some(f) => {
                let result = f();
                if result != 0 {
                    warn!(result, "player_play_pause callback returned non-zero");
                }
                Ok(())
            }
            None => Err("player_play_pause callback is null"),
        }
    }

    pub fn player_stop(&self) -> Result<(), &'static str> {
        match self.raw.player_stop {
            Some(f) => {
                let result = f();
                if result != 0 {
                    warn!(result, "player_stop callback returned non-zero");
                }
                Ok(())
            }
            None => Err("player_stop callback is null"),
        }
    }

    pub fn player_next(&self) -> Result<(), &'static str> {
        match self.raw.player_next {
            Some(f) => {
                let result = f();
                if result != 0 {
                    warn!(result, "player_next callback returned non-zero");
                }
                Ok(())
            }
            None => Err("player_next callback is null"),
        }
    }

    pub fn player_previous(&self) -> Result<(), &'static str> {
        match self.raw.player_previous {
            Some(f) => {
                let result = f();
                if result != 0 {
                    warn!(result, "player_previous callback returned non-zero");
                }
                Ok(())
            }
            None => Err("player_previous callback is null"),
        }
    }

    pub fn player_set_volume(&self, volume: i32) -> Result<(), &'static str> {
        match self.raw.player_set_volume {
            Some(f) => {
                let result = f(volume as c_int);
                if result != 0 {
                    warn!(
                        result,
                        volume, "player_set_volume callback returned non-zero"
                    );
                }
                Ok(())
            }
            None => Err("player_set_volume callback is null"),
        }
    }

    pub fn player_set_position(&self, position: i32) -> Result<(), &'static str> {
        match self.raw.player_set_position {
            Some(f) => {
                let result = f(position as c_int);
                if result != 0 {
                    warn!(
                        result,
                        position, "player_set_position callback returned non-zero"
                    );
                }
                Ok(())
            }
            None => Err("player_set_position callback is null"),
        }
    }

    // ── Fat callbacks (MessagePack) ──────────────────────────────────

    /// Executes a query via the `query_data` fat callback.
    /// Serializes `params` to MessagePack, sends to C#, deserializes the response.
    pub fn query<P: Serialize, R: DeserializeOwned>(
        &self,
        query_type: QueryType,
        params: &P,
    ) -> Result<R, String> {
        let query_fn = self
            .raw
            .query_data
            .ok_or_else(|| "query_data callback is null".to_string())?;

        let free_fn = self
            .raw
            .free_buffer
            .ok_or_else(|| "free_buffer callback is null".to_string())?;

        // Serialize params to msgpack as a NAMED map. MessagePack-CSharp's
        // ContractlessStandardResolver on the C# side reads structs as maps
        // keyed by property name; rmp_serde's plain `to_vec` writes them
        // positionally as arrays which fails with "Unexpected msgpack code
        // 147 (fixarray)" on the C# read.
        let params_buf = rmp_serde::to_vec_named(params)
            .map_err(|e| format!("Failed to serialize query params: {}", e))?;

        let mut result_buf: *mut u8 = std::ptr::null_mut();
        let mut result_len: u32 = 0;

        let status = query_fn(
            query_type as i32,
            params_buf.as_ptr(),
            params_buf.len() as u32,
            &mut result_buf,
            &mut result_len,
        );

        if status != 0 {
            // Free result buffer if it was allocated despite the error
            if !result_buf.is_null() {
                free_fn(result_buf);
            }
            return Err(format!("query_data callback returned error: {}", status));
        }

        if result_buf.is_null() || result_len == 0 {
            return Err("query_data returned null or empty result".to_string());
        }

        // Copy the result into a Rust-owned Vec before freeing
        let result_slice = unsafe { std::slice::from_raw_parts(result_buf, result_len as usize) };
        let result_vec = result_slice.to_vec();

        // Free the C#-allocated buffer
        free_fn(result_buf);

        // Deserialize from msgpack
        rmp_serde::from_slice(&result_vec)
            .map_err(|e| format!("Failed to deserialize query result: {}", e))
    }

    /// Executes a query that takes no parameters (sends empty msgpack array).
    pub fn query_no_params<R: DeserializeOwned>(&self, query_type: QueryType) -> Result<R, String> {
        self.query(query_type, &())
    }

    /// Executes a fire-and-forget command via the `execute_command` fat
    /// callback. `params` is serialized to MessagePack and handed to C#;
    /// any response buffer is freed and ignored.
    pub fn execute_command<P: Serialize>(
        &self,
        command_type: CommandType,
        params: &P,
    ) -> Result<(), String> {
        let exec_fn = self
            .raw
            .execute_command
            .ok_or_else(|| "execute_command callback is null".to_string())?;

        let free_fn = self
            .raw
            .free_buffer
            .ok_or_else(|| "free_buffer callback is null".to_string())?;

        // Same map-vs-array reasoning as `query` above — must use
        // to_vec_named so the C# contractless resolver can read the params.
        let params_buf = rmp_serde::to_vec_named(params)
            .map_err(|e| format!("Failed to serialize command params: {}", e))?;

        let mut result_buf: *mut u8 = std::ptr::null_mut();
        let mut result_len: u32 = 0;

        let status = exec_fn(
            command_type as i32,
            params_buf.as_ptr(),
            params_buf.len() as u32,
            &mut result_buf,
            &mut result_len,
        );

        if !result_buf.is_null() {
            free_fn(result_buf);
        }

        if status != 0 {
            return Err(format!(
                "execute_command callback returned error: {}",
                status
            ));
        }
        Ok(())
    }

    pub fn set_mute(&self, mute: bool) -> Result<(), String> {
        self.execute_command(CommandType::SetMute, &SetBoolParams { value: mute })
    }

    pub fn set_shuffle(&self, enabled: bool) -> Result<(), String> {
        self.execute_command(CommandType::SetShuffle, &SetBoolParams { value: enabled })
    }

    pub fn set_scrobble(&self, enabled: bool) -> Result<(), String> {
        self.execute_command(CommandType::SetScrobble, &SetBoolParams { value: enabled })
    }

    pub fn set_auto_dj(&self, enabled: bool) -> Result<(), String> {
        self.execute_command(CommandType::SetAutoDj, &SetBoolParams { value: enabled })
    }

    pub fn set_repeat(&self, mode: &str) -> Result<(), String> {
        self.execute_command(
            CommandType::SetRepeat,
            &SetRepeatParams {
                mode: mode.to_owned(),
            },
        )
    }

    pub fn set_rating(&self, rating: &str) -> Result<(), String> {
        self.execute_command(
            CommandType::SetRating,
            &StringValueParams {
                value: rating.to_owned(),
            },
        )
    }

    pub fn set_lfm_rating(&self, status: &str) -> Result<(), String> {
        self.execute_command(
            CommandType::SetLfmRating,
            &SetLfmRatingParams {
                status: status.to_owned(),
            },
        )
    }

    pub fn output_switch(&self, device: &str) -> Result<(), String> {
        self.execute_command(
            CommandType::OutputSwitch,
            &StringValueParams {
                value: device.to_owned(),
            },
        )
    }

    pub fn playlist_play(&self, url: &str) -> Result<(), String> {
        self.execute_command(
            CommandType::PlaylistPlay,
            &StringValueParams {
                value: url.to_owned(),
            },
        )
    }

    pub fn library_play_all(&self, shuffle: bool) -> Result<(), String> {
        self.execute_command(
            CommandType::LibraryPlayAll,
            &SetBoolParams { value: shuffle },
        )
    }

    pub fn now_playing_list_play(&self, path: &str) -> Result<(), String> {
        self.execute_command(
            CommandType::NowPlayingListPlay,
            &StringValueParams {
                value: path.to_owned(),
            },
        )
    }

    pub fn now_playing_list_move(&self, from: i32, to: i32) -> Result<(), String> {
        self.execute_command(CommandType::NowPlayingListMove, &MoveParams { from, to })
    }

    pub fn now_playing_list_remove(&self, index: i32) -> Result<(), String> {
        self.execute_command(CommandType::NowPlayingListRemove, &IndexParams { index })
    }

    pub fn now_playing_list_search(&self, query: &str) -> Result<(), String> {
        self.execute_command(
            CommandType::NowPlayingListSearch,
            &StringValueParams {
                value: query.to_owned(),
            },
        )
    }

    pub fn now_playing_queue(
        &self,
        queue_type: &str,
        files: Vec<String>,
        play: &str,
    ) -> Result<(), String> {
        self.execute_command(
            CommandType::NowPlayingQueue,
            &NowPlayingQueueParams {
                queue_type: queue_type.to_owned(),
                files,
                play: play.to_owned(),
            },
        )
    }

    pub fn query_playlists(&self) -> Result<PlaylistListResponse, String> {
        self.query_no_params(QueryType::PlaylistList)
    }

    pub fn query_now_playing_list(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<NowPlayingListResponse, String> {
        self.query(
            QueryType::NowPlayingList,
            &PaginationParams { offset, limit },
        )
    }

    /// iOS-v4 variant — list starts from the currently-playing track and
    /// `position` is the MusicBee-internal queue index (queue-absolute,
    /// not page-relative). Album / album_artist fields are populated.
    pub fn query_now_playing_list_ordered(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<NowPlayingListResponse, String> {
        self.query(
            QueryType::NowPlayingListOrdered,
            &PaginationParams { offset, limit },
        )
    }

    pub fn query_radio_stations(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<RadioStationsResponse, String> {
        self.query(
            QueryType::RadioStations,
            &PaginationParams { offset, limit },
        )
    }

    pub fn query_output_devices(&self) -> Result<OutputDevicesResponse, String> {
        self.query_no_params(QueryType::OutputDevices)
    }

    pub fn library_search_genres(&self, query: &str) -> Result<GenreListResponse, String> {
        self.query(
            QueryType::LibrarySearchGenre,
            &QueryParams {
                query: query.to_owned(),
            },
        )
    }

    pub fn library_search_artists(&self, query: &str) -> Result<ArtistListResponse, String> {
        self.query(
            QueryType::LibrarySearchArtist,
            &QueryParams {
                query: query.to_owned(),
            },
        )
    }

    pub fn library_search_albums(&self, query: &str) -> Result<AlbumListResponse, String> {
        self.query(
            QueryType::LibrarySearchAlbum,
            &QueryParams {
                query: query.to_owned(),
            },
        )
    }

    pub fn library_search_titles(&self, query: &str) -> Result<TrackListResponse, String> {
        self.query(
            QueryType::LibrarySearchTitle,
            &QueryParams {
                query: query.to_owned(),
            },
        )
    }

    pub fn library_browse_genres(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<GenreListResponse, String> {
        self.query(
            QueryType::LibraryBrowseGenres,
            &BrowseParams {
                offset,
                limit,
                album_artists: false,
            },
        )
    }

    pub fn library_browse_artists(
        &self,
        offset: i32,
        limit: i32,
        album_artists: bool,
    ) -> Result<ArtistListResponse, String> {
        self.query(
            QueryType::LibraryBrowseArtists,
            &BrowseParams {
                offset,
                limit,
                album_artists,
            },
        )
    }

    pub fn library_browse_albums(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<AlbumListResponse, String> {
        self.query(
            QueryType::LibraryBrowseAlbums,
            &BrowseParams {
                offset,
                limit,
                album_artists: false,
            },
        )
    }

    pub fn library_browse_tracks(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<TrackListResponse, String> {
        self.query(
            QueryType::LibraryBrowseTracks,
            &BrowseParams {
                offset,
                limit,
                album_artists: false,
            },
        )
    }

    pub fn library_genre_artists(&self, genre: &str) -> Result<ArtistListResponse, String> {
        self.query(
            QueryType::LibraryGenreArtists,
            &QueryParams {
                query: genre.to_owned(),
            },
        )
    }

    pub fn library_artist_albums(&self, artist: &str) -> Result<AlbumListResponse, String> {
        self.query(
            QueryType::LibraryArtistAlbums,
            &QueryParams {
                query: artist.to_owned(),
            },
        )
    }

    pub fn library_album_tracks(&self, album: &str) -> Result<TrackListResponse, String> {
        self.query(
            QueryType::LibraryAlbumTracks,
            &QueryParams {
                query: album.to_owned(),
            },
        )
    }

    pub fn query_now_playing_details(&self) -> Result<NowPlayingDetailsResponse, String> {
        self.query_no_params(QueryType::NowPlayingDetails)
    }

    pub fn query_album_cover(
        &self,
        artist: &str,
        album: &str,
        client_hash: &str,
    ) -> Result<AlbumCoverResponse, String> {
        self.query(
            QueryType::AlbumCover,
            &AlbumCoverParams {
                artist: artist.to_owned(),
                album: album.to_owned(),
                client_hash: client_hash.to_owned(),
            },
        )
    }

    pub fn query_cover_cache_build_status(&self) -> Result<CoverCacheBuildStatusResponse, String> {
        self.query_no_params(QueryType::CoverCacheBuildStatus)
    }

    pub fn query_album_cover_batch(
        &self,
        offset: i32,
        limit: i32,
    ) -> Result<AlbumCoverBatchResponse, String> {
        self.query(
            QueryType::AlbumCoverBatch,
            &PaginationParams { offset, limit },
        )
    }

    pub fn query_playback_position(&self) -> Result<PlaybackPositionResponse, String> {
        self.query_no_params(QueryType::PlaybackPosition)
    }

    pub fn library_queue(
        &self,
        target: LibraryQueueTarget,
        queue_type: &str,
        query: &str,
    ) -> Result<(), String> {
        let cmd = match target {
            LibraryQueueTarget::Genre => CommandType::LibraryQueueGenre,
            LibraryQueueTarget::Artist => CommandType::LibraryQueueArtist,
            LibraryQueueTarget::Album => CommandType::LibraryQueueAlbum,
            LibraryQueueTarget::Track => CommandType::LibraryQueueTrack,
        };
        self.execute_command(
            cmd,
            &LibraryQueueParams {
                queue_type: queue_type.to_owned(),
                query: query.to_owned(),
            },
        )
    }
}

/// Which meta-tag dimension a `LibraryQueue*` command targets.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
#[allow(dead_code)]
pub enum LibraryQueueTarget {
    Genre,
    Artist,
    Album,
    Track,
}
