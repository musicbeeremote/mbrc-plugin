//! The V4 wire codec. Owns every legacy-V4 spelling in one place: the
//! stringified `playervolume` inside `playerstatus`, the enum wire spellings
//! (`ShuffleMode::Off -> "off"`, PascalCase play/repeat), and the client input
//! value mappings (`"add-all" -> QueueType::AddAndPlay`). Isolating them here
//! keeps the canonical model clean and lets a V6 codec render/parse the same
//! data differently without touching handlers.

use serde_json::{json, Value};

use super::{sanitize_lyrics, strip_lrc, WireCodec};
use crate::protocol::messages::{
    Cover, LastfmStatus, Lyrics, NowPlayingListTrack, OutputDevices, Page, PlayState, PlayerState,
    QueueType, RepeatMode, ShuffleMode, TrackDetails, TrackInfo,
};
use crate::protocol::Platform;

/// The V4 wire codec (stateless).
pub struct V4Codec;

/// The shared V4 codec instance selected by `ProtocolVersion::V4`.
pub static V4_CODEC: V4Codec = V4Codec;

/// The file name of a path (last segment after `/` or `\`), for the V4
/// empty-title fallback (mirrors C# `Path.GetFileName`). An empty path stays empty.
fn file_name(path: &str) -> &str {
    path.rsplit(['/', '\\']).next().unwrap_or(path)
}

impl V4Codec {
    /// PascalCase play-state spelling, matching the shipped C# `PlayState`.
    fn play_state_str(state: PlayState) -> &'static str {
        match state {
            PlayState::Undefined => "Undefined",
            PlayState::Stopped => "Stopped",
            PlayState::Playing => "Playing",
            PlayState::Paused => "Paused",
        }
    }

    /// Lowercase shuffle spelling, matching the shipped C# `ShuffleState`.
    fn shuffle_str(mode: ShuffleMode) -> &'static str {
        match mode {
            ShuffleMode::Off => "off",
            ShuffleMode::Shuffle => "shuffle",
            ShuffleMode::AutoDj => "autodj",
        }
    }

    /// PascalCase repeat spelling, matching the shipped C# `RepeatMode`.
    fn repeat_str(mode: RepeatMode) -> &'static str {
        match mode {
            RepeatMode::Undefined => "Undefined",
            RepeatMode::None => "None",
            RepeatMode::All => "All",
            RepeatMode::One => "One",
        }
    }

    /// Last.fm status spelling, matching the shipped C# `LastfmStatus`.
    fn lfm_str(status: LastfmStatus) -> &'static str {
        match status {
            LastfmStatus::Normal => "Normal",
            LastfmStatus::Love => "Love",
            LastfmStatus::Ban => "Ban",
        }
    }
}

impl WireCodec for V4Codec {
    fn player_status(&self, state: &PlayerState) -> Value {
        // Field order matches the shipped C# plugin exactly (with preserve_order
        // on, `json!` key order is the wire order): repeat, mute, shuffle,
        // scrobbler, state, volume.
        json!({
            "playerrepeat": Self::repeat_str(state.repeat),
            "playermute": state.mute,
            "playershuffle": Self::shuffle_str(state.shuffle),
            "scrobbler": state.scrobble,
            "playerstate": Self::play_state_str(state.play_state),
            // V4 quirk: stringified here even though standalone playervolume is an int.
            "playervolume": state.volume.to_string(),
        })
    }

    fn output_devices(&self, devices: &OutputDevices) -> Value {
        json!({
            "active": devices.active,
            "devices": devices.devices,
        })
    }

    fn play_state(&self, state: PlayState) -> Value {
        json!(Self::play_state_str(state))
    }

    fn shuffle(&self, mode: ShuffleMode) -> Value {
        json!(Self::shuffle_str(mode))
    }

    fn repeat(&self, mode: RepeatMode) -> Value {
        json!(Self::repeat_str(mode))
    }

    fn lfm_status(&self, status: LastfmStatus) -> Value {
        json!(Self::lfm_str(status))
    }

    fn cover_notification(&self, _cover: &Cover) -> Value {
        // V4 broadcast = bare readiness marker; the client re-requests the image.
        json!({ "status": 1 })
    }

    fn lyrics(&self, lyrics: &Lyrics) -> Value {
        // Strip synchronized-lyrics timing first, then apply the V4 wire-safe
        // text formatting. Status is carried through (computed on the raw text).
        json!({
            "status": lyrics.status,
            "lyrics": sanitize_lyrics(&strip_lrc(&lyrics.lyrics)),
        })
    }

    fn now_playing_list(&self, page: &Page<NowPlayingListTrack>, platform: Platform) -> Value {
        // V4 quirk: iOS list items always carry album/album_artist (even when
        // empty); Android items never do. Not value-driven - purely the client.
        let with_album = platform != Platform::Android;
        let items: Vec<Value> = page
            .data
            .iter()
            .map(|t| {
                // V4 quirk (both variants): an empty title falls back to the file
                // name. Used to live in the C# host.
                let title = if t.title.is_empty() {
                    file_name(&t.path)
                } else {
                    t.title.as_str()
                };
                if with_album {
                    json!({
                        "artist": t.artist,
                        "album": t.album,
                        "album_artist": t.album_artist,
                        "title": title,
                        "path": t.path,
                        "position": t.position,
                    })
                } else {
                    json!({
                        // V4 quirk: the Android/sequential list also fills an empty
                        // artist with "Unknown Artist" (the iOS/ordered list does not).
                        "artist": if t.artist.is_empty() { "Unknown Artist" } else { t.artist.as_str() },
                        "title": title,
                        "path": t.path,
                        "position": t.position,
                    })
                }
            })
            .collect();
        json!({
            "total": page.total,
            "offset": page.offset,
            "limit": page.limit,
            "data": items,
        })
    }

    fn track_info(&self, info: &TrackInfo) -> Value {
        // Serialize the DTO, then fill the V4 empty-field defaults in place (so
        // the field order/names stay byte-identical to the plain serialization).
        let mut value = serde_json::to_value(info).unwrap_or_else(|_| json!({}));
        if let Some(obj) = value.as_object_mut() {
            if info.artist.is_empty() {
                obj.insert("artist".into(), Value::from("Unknown Artist"));
            }
            if info.album.is_empty() {
                obj.insert("album".into(), Value::from("Unknown Album"));
            }
            if info.title.is_empty() {
                obj.insert("title".into(), Value::from(file_name(&info.path)));
            }
        }
        value
    }

    fn track_details(&self, details: &TrackDetails) -> Value {
        let mut value = serde_json::to_value(details).unwrap_or_else(|_| json!({}));
        if details.album_artist.is_empty() {
            if let Some(obj) = value.as_object_mut() {
                obj.insert("albumArtist".into(), Value::from("Unknown Artist"));
            }
        }
        value
    }

    fn parse_repeat(&self, value: &str) -> Option<RepeatMode> {
        match value {
            "None" => Some(RepeatMode::None),
            "All" => Some(RepeatMode::All),
            "One" => Some(RepeatMode::One),
            _ => None,
        }
    }

    fn parse_lfm(&self, value: &str) -> Option<LastfmStatus> {
        // C# compares case-insensitively.
        match value.to_ascii_lowercase().as_str() {
            "love" => Some(LastfmStatus::Love),
            "ban" => Some(LastfmStatus::Ban),
            _ => None,
        }
    }

    fn parse_queue_type(&self, value: &str) -> QueueType {
        // Matches C# QueueTypeMapper (unrecognized -> Next).
        match value {
            "last" => QueueType::Last,
            "now" => QueueType::PlayNow,
            "add-all" => QueueType::AddAndPlay,
            _ => QueueType::Next,
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn player_status_stringifies_volume_only() {
        let state = PlayerState {
            play_state: PlayState::Playing,
            volume: 75,
            mute: false,
            shuffle: ShuffleMode::Off,
            repeat: RepeatMode::None,
            position: 0,
            scrobble: true,
        };
        let v = V4_CODEC.player_status(&state);
        assert_eq!(v["playervolume"], json!("75")); // string
        assert_eq!(v["playermute"], json!(false)); // still a bool
        assert_eq!(v["playerstate"], json!("Playing"));
        assert_eq!(v["playershuffle"], json!("off"));
        assert_eq!(v["playerrepeat"], json!("None"));
        assert_eq!(v["scrobbler"], json!(true));
    }

    #[test]
    fn enum_wire_spellings() {
        assert_eq!(V4_CODEC.play_state(PlayState::Paused), json!("Paused"));
        assert_eq!(V4_CODEC.shuffle(ShuffleMode::AutoDj), json!("autodj"));
        assert_eq!(V4_CODEC.repeat(RepeatMode::All), json!("All"));
        assert_eq!(V4_CODEC.lfm_status(LastfmStatus::Love), json!("Love"));
    }

    #[test]
    fn input_parsing() {
        assert_eq!(V4_CODEC.parse_repeat("All"), Some(RepeatMode::All));
        assert_eq!(V4_CODEC.parse_repeat("bogus"), None);
        assert_eq!(V4_CODEC.parse_lfm("LOVE"), Some(LastfmStatus::Love)); // case-insensitive
        assert_eq!(V4_CODEC.parse_lfm("toggle"), None); // handled by handler
        assert_eq!(V4_CODEC.parse_queue_type("add-all"), QueueType::AddAndPlay);
        assert_eq!(V4_CODEC.parse_queue_type("bogus"), QueueType::Next); // default
    }

    #[test]
    fn track_info_fills_v4_empty_defaults() {
        // Empty display fields fall back to the V4 quirks (the file name comes
        // from the path); non-empty values pass through untouched.
        let empty = TrackInfo {
            artist: String::new(),
            title: String::new(),
            album: String::new(),
            year: "2020".into(),
            path: "C:\\Music\\song.mp3".into(),
        };
        let v = V4_CODEC.track_info(&empty);
        assert_eq!(v["artist"], json!("Unknown Artist"));
        assert_eq!(v["album"], json!("Unknown Album"));
        assert_eq!(v["title"], json!("song.mp3"));
        assert_eq!(v["year"], json!("2020"));

        let full = TrackInfo {
            artist: "A".into(),
            title: "T".into(),
            album: "Al".into(),
            year: String::new(),
            path: "/x/y.flac".into(),
        };
        let v = V4_CODEC.track_info(&full);
        assert_eq!(v["artist"], json!("A"));
        assert_eq!(v["title"], json!("T"));
        assert_eq!(v["album"], json!("Al"));
    }

    #[test]
    fn track_details_fills_empty_album_artist() {
        let mut d = TrackDetails::default();
        assert_eq!(
            V4_CODEC.track_details(&d)["albumArtist"],
            json!("Unknown Artist")
        );
        d.album_artist = "Real".into();
        assert_eq!(V4_CODEC.track_details(&d)["albumArtist"], json!("Real"));
    }

    #[test]
    fn now_playing_list_android_defaults_artist_ios_does_not() {
        let page = Page {
            offset: 0,
            limit: 10,
            total: 1,
            data: vec![NowPlayingListTrack {
                artist: String::new(),
                title: "T".into(),
                path: "p".into(),
                position: 0,
                ..Default::default()
            }],
        };
        // Android (sequential): empty artist -> "Unknown Artist", no album keys.
        let a = V4_CODEC.now_playing_list(&page, Platform::Android);
        assert_eq!(a["data"][0]["artist"], json!("Unknown Artist"));
        assert!(a["data"][0].get("album").is_none());
        // iOS (ordered): raw empty artist, album keys present.
        let i = V4_CODEC.now_playing_list(&page, Platform::Ios);
        assert_eq!(i["data"][0]["artist"], json!(""));
        assert!(i["data"][0].get("album").is_some());
    }
}
