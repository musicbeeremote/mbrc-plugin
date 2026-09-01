//! Maps a MusicBee notification to the V6 event frames a subscriber receives.
//!
//! Parallel to [`notifications`](super::notifications), which builds the V4
//! frames; both are fed from the SAME cache snapshot per notification, so the
//! write side still queries MusicBee once.
//!
//! The player events are the three MusicBee actually notifies -
//! `play_state_changed`, `volume_changed`, `mute_changed` - the same live set as
//! V4. Track / now-playing / library events arrive with their own
//! later domains.

use serde_json::json;

use mbrc_wire::v6;

use super::commands_v6::player::play_state_str;
use crate::ffi::types::NotificationType;
use crate::nowplaying::NowPlaying;

/// Builds the V6 event frames for `ntype` from a cache snapshot.
///
/// Empty means there is nothing to send. Pure - no FFI - so it is unit-tested
/// against a `NowPlaying` literal, and an event carries only a marker or a light
/// summary: the client re-queries for the full typed state.
pub fn build(ntype: NotificationType, snap: &NowPlaying) -> Vec<String> {
    match ntype {
        NotificationType::PlayStateChanged => vec![v6::event(
            "play_state_changed",
            json!({ "play_state": play_state_str(snap.player.play_state) }),
        )],
        NotificationType::VolumeLevelChanged => {
            vec![v6::event(
                "volume_changed",
                json!({ "volume": snap.player.volume }),
            )]
        }
        NotificationType::VolumeMuteChanged => {
            vec![v6::event(
                "mute_changed",
                json!({ "muted": snap.player.mute }),
            )]
        }
        NotificationType::TrackChanged => vec![v6::event(
            "now_playing_changed",
            json!({
                "artist": snap.track_info.artist,
                "title": snap.track_info.title,
                "album": snap.track_info.album,
                "path": snap.track_info.path,
            }),
        )],
        // Lyrics load asynchronously; re-querying does the synced fetch + parse.
        NotificationType::NowPlayingLyricsReady => {
            vec![v6::event("now_playing_lyrics_changed", json!({}))]
        }
        NotificationType::NowPlayingListChanged => {
            vec![v6::event("now_playing_list_changed", json!({}))]
        }
        // `library_changed` and `cover_cache_changed` are emitted imperatively,
        // from the dispatch and reconcile paths, which own the `Arc<Core>`.
        NotificationType::NowPlayingArtworkReady
        | NotificationType::FileAddedToLibrary
        | NotificationType::LibrarySwitched => Vec::new(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{PlayState, PlayerState};
    use serde_json::Value;

    fn snap(player: PlayerState) -> NowPlaying {
        NowPlaying {
            player,
            ..Default::default()
        }
    }

    fn one(frames: &[String]) -> Value {
        assert_eq!(frames.len(), 1, "expected exactly one event frame");
        serde_json::from_str(&frames[0]).unwrap()
    }

    #[test]
    fn play_state_event_is_typed_string() {
        let frames = build(
            NotificationType::PlayStateChanged,
            &snap(PlayerState {
                play_state: PlayState::Paused,
                ..Default::default()
            }),
        );
        let v = one(&frames);
        assert_eq!(v["kind"], "event");
        assert_eq!(v["event"], "play_state_changed");
        assert_eq!(v["data"]["play_state"], "paused");
    }

    #[test]
    fn volume_and_mute_events_are_typed() {
        let s = snap(PlayerState {
            volume: 33,
            mute: true,
            ..Default::default()
        });
        let vol = one(&build(NotificationType::VolumeLevelChanged, &s));
        assert_eq!(vol["event"], "volume_changed");
        assert_eq!(vol["data"]["volume"], 33); // int, not stringified

        let mute = one(&build(NotificationType::VolumeMuteChanged, &s));
        assert_eq!(mute["event"], "mute_changed");
        assert_eq!(mute["data"]["muted"], true);
    }

    #[test]
    fn track_changed_emits_now_playing_summary() {
        use crate::protocol::messages::TrackInfo;
        let s = NowPlaying {
            track_info: TrackInfo {
                artist: "A".into(),
                title: "T".into(),
                album: "Al".into(),
                path: "p.mp3".into(),
                ..Default::default()
            },
            ..Default::default()
        };
        let v = one(&build(NotificationType::TrackChanged, &s));
        assert_eq!(v["event"], "now_playing_changed");
        assert_eq!(v["data"]["title"], "T");
        assert_eq!(v["data"]["path"], "p.mp3");
    }

    #[test]
    fn lyrics_ready_emits_marker() {
        let s = NowPlaying::default();
        let v = one(&build(NotificationType::NowPlayingLyricsReady, &s));
        assert_eq!(v["event"], "now_playing_lyrics_changed");
    }

    #[test]
    fn list_changed_emits_marker() {
        let s = NowPlaying::default();
        let v = one(&build(NotificationType::NowPlayingListChanged, &s));
        assert_eq!(v["event"], "now_playing_list_changed");
    }

    #[test]
    fn still_silent_notifications_emit_nothing() {
        // FileAdded/LibrarySwitched emit `library_changed` imperatively from the
        // dispatch path, not from this pure builder.
        let s = NowPlaying::default();
        assert!(build(NotificationType::FileAddedToLibrary, &s).is_empty());
        assert!(build(NotificationType::LibrarySwitched, &s).is_empty());
        assert!(build(NotificationType::NowPlayingArtworkReady, &s).is_empty());
    }
}
