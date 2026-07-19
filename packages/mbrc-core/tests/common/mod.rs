//! Shared test fixtures: a fully-populated `Providers` returning representative
//! data so every response field of every domain is observable. Used by both the
//! V4 golden-replay schema test and the V6 golden-snapshot test.
//!
//! `tracks_detailed_for_paths` returns a populated `TrackTags` (the V6 canonical
//! track source); the V4 golden path does not use it, so this is additive.

#![allow(dead_code)]

use mbrc_core::protocol::messages::*;
use mbrc_core::providers::Providers;

/// A provider returning fully-populated representative data so every field of
/// every response schema is observable.
pub struct FixtureProviders;

fn track() -> Track {
    Track {
        src: "C:\\Music\\s.mp3".into(),
        artist: "Artist".into(),
        title: "Title".into(),
        trackno: 1,
        disc: 1,
        album: "Album".into(),
        album_artist: "AlbumArtist".into(),
        genre: "Rock".into(),
    }
}

impl Providers for FixtureProviders {
    fn play(&self) -> Result<(), String> {
        Ok(())
    }
    fn pause(&self) -> Result<(), String> {
        Ok(())
    }
    fn play_pause(&self) -> Result<(), String> {
        Ok(())
    }
    fn stop(&self) -> Result<(), String> {
        Ok(())
    }
    fn next(&self) -> Result<(), String> {
        Ok(())
    }
    fn previous(&self) -> Result<(), String> {
        Ok(())
    }
    fn set_volume(&self, _v: i32) -> Result<(), String> {
        Ok(())
    }
    fn set_position(&self, _v: i32) -> Result<(), String> {
        Ok(())
    }
    fn player_state(&self) -> Result<PlayerState, String> {
        Ok(PlayerState {
            play_state: PlayState::Playing,
            volume: 75,
            mute: false,
            shuffle: ShuffleMode::Off,
            repeat: RepeatMode::None,
            position: 1000,
            scrobble: true,
        })
    }
    fn set_mute(&self, _v: bool) -> Result<(), String> {
        Ok(())
    }
    fn set_shuffle(&self, _v: bool) -> Result<(), String> {
        Ok(())
    }
    fn set_auto_dj(&self, _v: bool) -> Result<(), String> {
        Ok(())
    }
    fn set_repeat(&self, _m: RepeatMode) -> Result<(), String> {
        Ok(())
    }
    fn set_scrobble(&self, _v: bool) -> Result<(), String> {
        Ok(())
    }
    fn output_devices(&self) -> Result<OutputDevices, String> {
        Ok(OutputDevices {
            active: "Speakers".into(),
            devices: vec!["Speakers".into(), "Headphones".into()],
        })
    }
    fn switch_output(&self, _d: &str) -> Result<(), String> {
        Ok(())
    }
    fn playback_position(&self) -> Result<PlaybackPositionResponse, String> {
        Ok(PlaybackPositionResponse {
            current: 1000,
            total: 240000,
        })
    }
    fn track_info(&self) -> Result<TrackInfo, String> {
        Ok(TrackInfo {
            artist: "Artist".into(),
            title: "Title".into(),
            album: "Album".into(),
            year: "2024".into(),
            path: "C:\\Music\\s.mp3".into(),
        })
    }
    fn track_details(&self) -> Result<TrackDetails, String> {
        Ok(TrackDetails {
            album_artist: "AlbumArtist".into(),
            genre: "Rock".into(),
            track_no: "1".into(),
            track_count: "10".into(),
            disc_no: "1".into(),
            disc_count: "1".into(),
            publisher: "Label".into(),
            composer: "Composer".into(),
            comment: "c".into(),
            grouping: "g".into(),
            rating_album: "5".into(),
            encoder: "LAME".into(),
            kind: "mp3".into(),
            format: "MPEG".into(),
            size: "1".into(),
            channels: "2".into(),
            sample_rate: "44100".into(),
            bitrate: "320".into(),
            date_modified: "2024".into(),
            date_added: "2024".into(),
            last_played: "2024".into(),
            play_count: "1".into(),
            skip_count: "0".into(),
            duration: "240000".into(),
        })
    }
    fn cover(&self) -> Result<Cover, String> {
        Ok(Cover {
            status: 200,
            cover: "base64".into(),
        })
    }
    fn lyrics(&self) -> Result<Lyrics, String> {
        Ok(Lyrics {
            status: 200,
            lyrics: "la la".into(),
        })
    }
    fn now_playing_synced_lyrics(&self) -> Result<Lyrics, String> {
        Ok(Lyrics {
            status: 200,
            lyrics: "la la".into(),
        })
    }
    fn rating(&self) -> Result<String, String> {
        Ok("4".into())
    }
    fn set_rating(&self, _v: &str) -> Result<(), String> {
        Ok(())
    }
    fn lfm_rating(&self) -> Result<LastfmStatus, String> {
        Ok(LastfmStatus::Love)
    }
    fn set_lfm_rating(&self, _s: LastfmStatus) -> Result<(), String> {
        Ok(())
    }
    fn has_lastfm_account(&self) -> Result<bool, String> {
        Ok(true)
    }
    fn set_tag(&self, _t: &str, _v: &str) -> Result<(), String> {
        Ok(())
    }
    fn now_playing_list(&self, o: i32, l: i32) -> Result<Page<NowPlayingListTrack>, String> {
        Ok(Page {
            offset: o,
            limit: l,
            total: 1,
            data: vec![NowPlayingListTrack {
                artist: "Artist".into(),
                album: "Album".into(),
                album_artist: "AlbumArtist".into(),
                title: "Title".into(),
                path: "C:\\Music\\s.mp3".into(),
                position: 0,
            }],
        })
    }
    fn now_playing_list_ordered(
        &self,
        o: i32,
        l: i32,
    ) -> Result<Page<NowPlayingListTrack>, String> {
        self.now_playing_list(o, l)
    }
    fn play_list_item(&self, _i: i32) -> Result<(), String> {
        Ok(())
    }
    fn remove_list_item(&self, _i: i32) -> Result<(), String> {
        Ok(())
    }
    fn move_list_item(&self, _f: i32, _t: i32) -> Result<(), String> {
        Ok(())
    }
    fn search_list(&self, _q: &str) -> Result<(), String> {
        Ok(())
    }
    fn queue(&self, _q: QueueType, _f: Vec<String>, _p: &str) -> Result<(), String> {
        Ok(())
    }
    fn browse_genres(&self, o: i32, l: i32) -> Result<Page<GenreData>, String> {
        Ok(Page {
            offset: o,
            limit: l,
            total: 1,
            data: vec![GenreData {
                genre: "Rock".into(),
                count: 5,
            }],
        })
    }
    fn browse_artists(&self, o: i32, l: i32, _aa: bool) -> Result<Page<ArtistData>, String> {
        Ok(Page {
            offset: o,
            limit: l,
            total: 1,
            data: vec![ArtistData {
                artist: "Artist".into(),
                count: 3,
            }],
        })
    }
    fn browse_albums(&self, o: i32, l: i32) -> Result<Page<AlbumData>, String> {
        Ok(Page {
            offset: o,
            limit: l,
            total: 1,
            data: vec![AlbumData {
                album: "Album".into(),
                artist: "Artist".into(),
                count: 12,
            }],
        })
    }
    fn browse_tracks(&self, o: i32, l: i32) -> Result<Page<Track>, String> {
        Ok(Page {
            offset: o,
            limit: l,
            total: 1,
            data: vec![track()],
        })
    }
    fn genre_artists(&self, _g: &str) -> Result<Vec<ArtistData>, String> {
        Ok(vec![ArtistData {
            artist: "Artist".into(),
            count: 3,
        }])
    }
    fn artist_albums(&self, _a: &str) -> Result<Vec<AlbumData>, String> {
        Ok(vec![AlbumData {
            album: "Album".into(),
            artist: "Artist".into(),
            count: 12,
        }])
    }
    fn album_tracks(&self, _a: &str) -> Result<Vec<Track>, String> {
        // iOS libraryalbumtracks omits album/genre - leave them empty.
        Ok(vec![Track {
            src: "C:\\Music\\s.mp3".into(),
            artist: "Artist".into(),
            title: "Title".into(),
            trackno: 1,
            disc: 1,
            album_artist: "AlbumArtist".into(),
            ..Default::default()
        }])
    }
    fn album_cover(&self, _ar: &str, _al: &str, _h: &str) -> Result<AlbumCover, String> {
        Ok(AlbumCover {
            status: 200,
            artist: "Artist".into(),
            album: "Album".into(),
            cover: "base64".into(),
            hash: "sha1".into(),
        })
    }
    fn album_cover_page(&self, o: i32, l: i32) -> Result<Page<AlbumCoverItem>, String> {
        Ok(Page {
            offset: o,
            limit: l,
            total: 1,
            data: vec![AlbumCoverItem {
                album: "Album".into(),
                artist: "Artist".into(),
                cover: "base64".into(),
                status: 200,
                hash: "sha1".into(),
            }],
        })
    }
    fn cover_cache_status(&self) -> Result<bool, String> {
        Ok(false)
    }
    fn set_background_task_message(&self, _message: &str) -> Result<(), String> {
        Ok(())
    }
    fn album_identifiers(&self) -> Result<Vec<AlbumIdentifier>, String> {
        Ok(vec![AlbumIdentifier {
            artist: "AlbumArtist".into(),
            album: "Album".into(),
            path: "C:\\Music\\s.mp3".into(),
            modified: 0,
        }])
    }
    fn artwork_raw(&self, _path: &str) -> Result<String, String> {
        Ok(String::new())
    }
    fn batch_metadata(&self, _paths: Vec<String>) -> Result<Vec<TrackMetadata>, String> {
        Ok(vec![TrackMetadata {
            path: "C:\\Music\\s.mp3".into(),
            artist: "AlbumArtist".into(),
            album: "Album".into(),
        }])
    }
    fn track_paths(&self) -> Result<Vec<String>, String> {
        Ok(vec!["C:\\Music\\s.mp3".into()])
    }
    fn tracks_for_paths(&self, _paths: Vec<String>) -> Result<Vec<Track>, String> {
        Ok(vec![track()])
    }
    fn tracks_detailed_for_paths(&self, paths: Vec<String>) -> Result<Vec<TrackTags>, String> {
        // One populated canonical track per requested path (the V6 track schema
        // source). Typed fields carry raw values the core parses: year from a
        // full date, duration "m:ss", rating, ISO date_added.
        Ok(paths
            .into_iter()
            .map(|src| TrackTags {
                src,
                artist: "Artist".into(),
                title: "Title".into(),
                album: "Album".into(),
                album_artist: "AlbumArtist".into(),
                track_no: 1,
                disc_no: 1,
                genre: "Rock".into(),
                year: "12/03/2007".into(),
                duration: "4:00".into(),
                rating: "4.5".into(),
                date_added: "2024-01-02T03:04:05Z".into(),
            })
            .collect())
    }
    fn sync_delta(&self, _updated_since: i64) -> Result<SyncDelta, String> {
        Ok(SyncDelta::default())
    }
    fn radio_stations(&self, o: i32, l: i32) -> Result<Page<RadioStation>, String> {
        Ok(Page {
            offset: o,
            limit: l,
            total: 1,
            data: vec![RadioStation {
                name: "Radio".into(),
                url: "http://s".into(),
            }],
        })
    }
    fn play_all(&self, _s: bool) -> Result<(), String> {
        Ok(())
    }
    fn playlists(&self, o: i32, l: i32) -> Result<Page<Playlist>, String> {
        Ok(Page {
            offset: o,
            limit: l,
            total: 1,
            data: vec![Playlist {
                url: "playlist://x".into(),
                name: "X".into(),
            }],
        })
    }
    fn play_playlist(&self, _u: &str) -> Result<(), String> {
        Ok(())
    }
    fn plugin_version(&self) -> Result<String, String> {
        Ok("1.4.0".into())
    }
}
