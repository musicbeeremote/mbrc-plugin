//! Maps a MusicBee notification to the broadcast frames clients receive.
//!
//! The notification is the write side of the now-playing cache: it first
//! refreshes the affected cache slice through the provider RPC, then builds the
//! broadcast frames from the refreshed snapshot. So a track change queries
//! MusicBee once - the same cache the client's `init`/`nowplaying*` requests
//! read - instead of once for the broadcast and again per request.
//!
//! Mirrors the C# `NotificationHandler`:
//! - TrackChanged -> track + cover + lyrics + rating + lfm-rating + position
//! - PlayStateChanged -> playerstate; Volume/Mute -> playervolume/playermute
//! - LyricsReady/ArtworkReady -> lyrics/cover; ListChanged -> nowplayinglistchanged
//! - FileAddedToLibrary -> no broadcast (caches the cover, C#-side)

use serde_json::{json, Value};

use crate::ffi::types::NotificationType;
use crate::nowplaying::NowPlaying;
use crate::protocol::messages::Cover;
use crate::protocol::version::ProtocolVersion;
use crate::state::Core;
use crate::wire::WireCodec;

/// Refresh the cache for `ntype`, then build the broadcast frames from the
/// refreshed snapshot - returning `(v4_frames, v6_frames)`. The single refresh +
/// snapshot feeds both protocols' fan-out. The entry point used by
/// `state::handle_notification`.
pub fn on_notification(core: &Core, ntype: NotificationType) -> (Vec<String>, Vec<String>) {
    // Write side: refresh only the slice this notification touches. Position is
    // never cached, so it has no refresh here.
    match ntype {
        NotificationType::TrackChanged => core.now_playing.refresh_track_bundle(),
        NotificationType::PlayStateChanged
        | NotificationType::VolumeLevelChanged
        | NotificationType::VolumeMuteChanged => core.now_playing.refresh_player(),
        NotificationType::NowPlayingLyricsReady => core.now_playing.refresh_lyrics(),
        NotificationType::NowPlayingArtworkReady => core.now_playing.refresh_cover(),
        // ListChanged/FileAdded/LibrarySwitched touch no now-playing slice.
        // Library changes maintain the metadata cache in `state::handle_notification`
        // (which needs the owned `Arc<Core>` to spawn the reconcile).
        NotificationType::NowPlayingListChanged
        | NotificationType::FileAddedToLibrary
        | NotificationType::LibrarySwitched => {}
    }

    // Position rides along with the track-change bundle but is not cached (it
    // advances continuously); fetch it fresh just for that broadcast.
    let position = if ntype == NotificationType::TrackChanged {
        core.providers
            .playback_position()
            .ok()
            .and_then(|p| serde_json::to_value(&p).ok())
    } else {
        None
    };

    let snapshot = core.now_playing.snapshot();
    let v4 = build(ntype, &snapshot, position);
    let v6 = super::notifications_v6::build(ntype, &snapshot);
    (v4, v6)
}

/// Build the raw broadcast frames from a cache snapshot (empty = nothing to
/// send). Pure - no FFI - so it is unit-tested against a `NowPlaying` literal.
fn build(ntype: NotificationType, snap: &NowPlaying, position: Option<Value>) -> Vec<String> {
    let mut out: Vec<(String, Value)> = Vec::new();
    // Broadcasts are V4-formatted; per-client version fan-out is a V6 concern.
    let wire = ProtocolVersion::V4.codec();
    match ntype {
        NotificationType::TrackChanged => {
            out.push((
                "nowplayingtrack".to_string(),
                wire.track_info(&snap.track_info),
            ));
            push_cover(&mut out, wire, &snap.cover);
            out.push(("nowplayinglyrics".to_string(), wire.lyrics(&snap.lyrics)));
            out.push((
                "nowplayingrating".to_string(),
                Value::from(snap.rating.clone()),
            ));
            out.push(("nowplayinglfmrating".to_string(), wire.lfm_status(snap.lfm)));
            if let Some(position) = position {
                out.push(("nowplayingposition".to_string(), position));
            }
        }
        NotificationType::PlayStateChanged => {
            out.push((
                "playerstate".to_string(),
                wire.play_state(snap.player.play_state),
            ));
        }
        NotificationType::VolumeLevelChanged => {
            out.push(("playervolume".to_string(), Value::from(snap.player.volume)));
        }
        NotificationType::VolumeMuteChanged => {
            out.push(("playermute".to_string(), Value::from(snap.player.mute)));
        }
        NotificationType::NowPlayingLyricsReady => {
            out.push(("nowplayinglyrics".to_string(), wire.lyrics(&snap.lyrics)));
        }
        NotificationType::NowPlayingArtworkReady => {
            push_cover(&mut out, wire, &snap.cover);
        }
        NotificationType::NowPlayingListChanged => {
            out.push(("nowplayinglistchanged".to_string(), json!(true)));
        }
        // No self-emitted frame: FileAdded is cache maintenance only, and the
        // LibrarySwitched reconcile broadcasts its own cover-cache build status.
        NotificationType::FileAddedToLibrary | NotificationType::LibrarySwitched => {}
    }
    out.into_iter()
        .map(|(ctx, data)| frame(&ctx, data))
        .collect()
}

/// Broadcast the cover as a `{status:1}` readiness marker when artwork is
/// present (status 200), so the client re-requests the image. No art -> no
/// frame (the client keeps showing nothing new), matching the shipped plugin.
fn push_cover(out: &mut Vec<(String, Value)>, wire: &dyn WireCodec, cover: &Cover) {
    if cover.status == 200 {
        out.push((
            "nowplayingcover".to_string(),
            wire.cover_notification(cover),
        ));
    } else {
        tracing::debug!("no now-playing artwork; skipping cover broadcast");
    }
}

/// Build a raw `{"context":..,"data":..}` frame (shared with the monitor).
pub(crate) fn frame(context: &str, data: Value) -> String {
    serde_json::to_string(&json!({ "context": context, "data": data }))
        .expect("serializing a broadcast frame cannot fail")
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{Lyrics, PlayState, PlayerState};

    fn contexts(frames: &[String]) -> Vec<String> {
        frames
            .iter()
            .map(|f| {
                serde_json::from_str::<Value>(f).unwrap()["context"]
                    .as_str()
                    .unwrap()
                    .to_string()
            })
            .collect()
    }

    #[test]
    fn track_changed_bundles_six_frames() {
        let snap = NowPlaying {
            cover: Cover {
                status: 200,
                cover: "art".into(),
            },
            ..Default::default()
        };
        let frames = build(
            NotificationType::TrackChanged,
            &snap,
            Some(json!({"current": 0, "total": 0})),
        );
        assert_eq!(
            contexts(&frames),
            vec![
                "nowplayingtrack",
                "nowplayingcover",
                "nowplayinglyrics",
                "nowplayingrating",
                "nowplayinglfmrating",
                "nowplayingposition",
            ]
        );
        // The cover broadcast is the bare readiness marker (no inline data).
        let cover: Value = serde_json::from_str(&frames[1]).unwrap();
        assert_eq!(
            cover,
            json!({"context": "nowplayingcover", "data": {"status": 1}})
        );
    }

    #[test]
    fn cover_broadcast_skipped_when_no_artwork() {
        let snap = NowPlaying::default(); // default cover status = 0 (no art)
        let frames = build(NotificationType::NowPlayingArtworkReady, &snap, None);
        assert!(frames.is_empty());
    }

    #[test]
    fn play_state_and_volume_and_mute() {
        let snap = NowPlaying {
            player: PlayerState {
                play_state: PlayState::Paused,
                volume: 33,
                mute: true,
                ..Default::default()
            },
            ..Default::default()
        };
        let ps = build(NotificationType::PlayStateChanged, &snap, None);
        let v: Value = serde_json::from_str(&ps[0]).unwrap();
        assert_eq!(v, json!({"context": "playerstate", "data": "Paused"}));

        let vol = build(NotificationType::VolumeLevelChanged, &snap, None);
        let v: Value = serde_json::from_str(&vol[0]).unwrap();
        assert_eq!(v, json!({"context": "playervolume", "data": 33})); // int

        let mute = build(NotificationType::VolumeMuteChanged, &snap, None);
        let v: Value = serde_json::from_str(&mute[0]).unwrap();
        assert_eq!(v, json!({"context": "playermute", "data": true}));
    }

    #[test]
    fn lyrics_ready_broadcasts_lyrics() {
        let snap = NowPlaying {
            lyrics: Lyrics {
                status: 200,
                lyrics: "la la".into(),
            },
            ..Default::default()
        };
        let frames = build(NotificationType::NowPlayingLyricsReady, &snap, None);
        assert_eq!(contexts(&frames), vec!["nowplayinglyrics"]);
    }

    #[test]
    fn list_changed_and_file_added() {
        let snap = NowPlaying::default();
        assert_eq!(
            contexts(&build(NotificationType::NowPlayingListChanged, &snap, None)),
            vec!["nowplayinglistchanged"]
        );
        assert!(build(NotificationType::FileAddedToLibrary, &snap, None).is_empty());
    }
}
