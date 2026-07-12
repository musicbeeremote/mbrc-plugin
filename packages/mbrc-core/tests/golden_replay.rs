//! Golden-replay schema conformance: drive each committed golden's client->server
//! frames at the real core (backed by a fully-populated fixture provider), plus
//! the broadcast notifications, and assert the core's server->client wire
//! *schema* covers the golden's for every context. This is the capstone proving
//! the Rust core's wire shapes match the shipped C# plugin.
//!
//! Schema (field names + types), not values - value parity (Layer 3) is out of
//! scope for this milestone. The check is directional: every field the golden
//! shows must appear in the core's output with the same type (the core may add
//! optional fields the golden happened not to exercise).

use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::sync::Arc;
use std::time::Duration;

use serde_json::Value;

use mbrc_capture::{endpoint_schemas, Frame};
use mbrc_core::config::Config;
use mbrc_core::ffi::types::NotificationType;
use mbrc_core::protocol::messages::*;
use mbrc_core::providers::Providers;
use mbrc_core::server;
use mbrc_core::state::Core;

/// Contexts the golden has as s2c but the core legitimately never sends:
/// `ping` is a server-initiated keepalive the core does not originate.
const EXCLUDED_S2C: &[&str] = &["ping"];

/// A provider returning fully-populated representative data so every field of
/// every response schema is observable.
struct FixtureProviders;

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

fn free_port() -> u16 {
    std::net::TcpListener::bind("127.0.0.1:0")
        .unwrap()
        .local_addr()
        .unwrap()
        .port()
}

/// Client->server command frames from a golden (excluding handshake/keepalive),
/// plus the client type declared in its `player` frame.
fn golden_c2s(contents: &str) -> (String, Vec<String>) {
    let mut client_type = "Android".to_string();
    let mut commands = Vec::new();
    for line in contents.lines() {
        let Ok(record) = serde_json::from_str::<Value>(line) else {
            continue;
        };
        if record.get("dir").and_then(Value::as_str) != Some("c2s") {
            continue;
        }
        let frame = &record["frame"];
        let context = frame.get("context").and_then(Value::as_str).unwrap_or("");
        let raw = record.get("raw").and_then(Value::as_str).unwrap_or("");
        match context {
            "player" => {
                if let Some(ct) = frame.get("data").and_then(Value::as_str) {
                    client_type = ct.to_string();
                }
            }
            "protocol" | "pong" | "" => {}
            _ => commands.push(raw.to_string()),
        }
    }
    (client_type, commands)
}

/// Replay a golden's c2s + all notifications at the core, returning the collected
/// s2c frames as an `mbrc-capture/2` JSONL string for schema extraction.
fn replay(core: Arc<Core>, port: u16, client_type: &str, commands: &[String]) -> String {
    let mut writer = TcpStream::connect(("127.0.0.1", port)).unwrap();
    let mut reader = BufReader::new(writer.try_clone().unwrap());
    writer
        .try_clone()
        .unwrap()
        .set_read_timeout(Some(Duration::from_millis(400)))
        .unwrap();
    reader
        .get_ref()
        .set_read_timeout(Some(Duration::from_millis(400)))
        .unwrap();

    // Handshake, then read the two replies so we know we're registered.
    writer
        .write_all(format!("{{\"context\":\"player\",\"data\":\"{client_type}\"}}\r\n").as_bytes())
        .unwrap();
    writer
        .write_all(b"{\"context\":\"protocol\",\"data\":{\"protocol_version\":4,\"no_broadcast\":false}}\r\n")
        .unwrap();

    let mut s2c: Vec<String> = Vec::new();
    for _ in 0..2 {
        if let Some(line) = read_frame(&mut reader) {
            s2c.push(line);
        }
    }

    // Drive every command, then fan out every notification kind.
    for cmd in commands {
        writer.write_all(cmd.as_bytes()).unwrap();
        writer.write_all(b"\r\n").unwrap();
    }
    for ntype in [
        NotificationType::TrackChanged,
        NotificationType::PlayStateChanged,
        NotificationType::VolumeLevelChanged,
        NotificationType::VolumeMuteChanged,
        NotificationType::NowPlayingLyricsReady,
        NotificationType::NowPlayingArtworkReady,
        NotificationType::NowPlayingListChanged,
        NotificationType::FileAddedToLibrary,
        NotificationType::LibrarySwitched,
    ] {
        let frames = server::notifications::on_notification(&core, ntype);
        core.broadcaster.broadcast(&frames);
    }

    // Drain everything else until the read times out.
    while let Some(line) = read_frame(&mut reader) {
        s2c.push(line);
    }

    // Wrap each collected frame as an mbrc-capture/2 s2c record.
    s2c.iter()
        .enumerate()
        .map(|(i, raw)| {
            let frame = Frame::new(0, i as u64, "s2c", 0, raw.trim().as_bytes());
            serde_json::to_string(&frame).unwrap()
        })
        .collect::<Vec<_>>()
        .join("\n")
}

/// Read one CRLF-terminated frame, or `None` on timeout/EOF.
fn read_frame(reader: &mut BufReader<TcpStream>) -> Option<String> {
    let mut line = String::new();
    match reader.read_line(&mut line) {
        Ok(0) => None,
        Ok(_) => Some(line),
        Err(_) => None, // timeout
    }
}

fn assert_golden_covered(golden_path: &str, client_type_hint: &str) {
    let golden = std::fs::read_to_string(golden_path).unwrap();
    let (client_type, commands) = golden_c2s(&golden);
    assert_eq!(client_type, client_type_hint);

    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(FixtureProviders),
        Config {
            port,
            ..Config::default()
        },
    ));
    // The paginated `libraryalbumcover` is served from the CoverStore. The test
    // Config has no storage path, so the background build is skipped; pre-warm
    // one album so the page yields an item (all fields present) deterministically.
    core.cover_store
        .warm_up(&[mbrc_core::cover::store::AlbumIdentity {
            key: mbrc_core::cover::cover_identifier("AlbumArtist", "Album"),
            path: "C:\\Music\\s.mp3".into(),
            modified: 0,
        }]);
    let net = server::start(core.clone()).expect("start");
    let replayed = replay(core, port, &client_type, &commands);
    net.stop();

    let golden_schemas = endpoint_schemas(&golden);
    let core_schemas = endpoint_schemas(&replayed);

    let mut problems: Vec<String> = Vec::new();
    for ((dir, context), golden_fields) in &golden_schemas {
        if dir != "s2c" || EXCLUDED_S2C.contains(&context.as_str()) {
            continue;
        }
        let Some(core_fields) = core_schemas.get(&(dir.clone(), context.clone())) else {
            problems.push(format!("core never produced s2c `{context}`"));
            continue;
        };
        for (field, gtype) in golden_fields {
            match core_fields.get(field) {
                None => problems.push(format!("`{context}`: missing field `{field}` ({gtype})")),
                Some(ctype) if ctype != gtype => problems.push(format!(
                    "`{context}`: field `{field}` type {gtype} (golden) vs {ctype} (core)"
                )),
                Some(_) => {}
            }
        }
    }

    assert!(
        problems.is_empty(),
        "{golden_path} schema conformance failures:\n  {}",
        problems.join("\n  ")
    );
}

#[test]
fn android_golden_schema_conformance() {
    assert_golden_covered("../../tests/golden/legacy-v4-android.jsonl", "Android");
}

#[test]
fn ios_golden_schema_conformance() {
    assert_golden_covered("../../tests/golden/legacy-v4-ios.jsonl", "iOS");
}
