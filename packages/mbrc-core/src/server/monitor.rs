//! Poll-driven broadcasts (the C# `StateMonitor` port).
//!
//! Some state changes fire no MusicBee event: playback position advances
//! continuously, a user can change shuffle/repeat/scrobble in MusicBee's own UI,
//! and stop-after-current clears itself when it fires without announcing it. A
//! timer task polls the provider RPC, broadcasts `nowplayingposition` while
//! playing, and broadcasts the four modes only when they change.
//!
//! Stop-after-current is the one MusicBee does notify about, on the way in. The
//! event is emitted here anyway, and only here, so there is one source rather
//! than a notification and a poll racing to say the same thing a second apart -
//! and so the silent clear is announced like any other change.
//!
//! Both protocols hear it. This poll is the only place those three changes are
//! ever noticed, so a version it does not speak has no other way to learn them:
//! a V6 client saw its own writes and nothing a user did in MusicBee.
//!
//! Only polls while at least one client is connected, so an idle core makes no
//! FFI calls.

use std::sync::Arc;
use std::time::Duration;

use serde_json::json;
use tokio::sync::Notify;

use mbrc_wire::v6;

use super::commands_v6::player::{repeat_str, shuffle_str};
use super::notifications::frame;
use crate::nowplaying::NowPlayingCache;
use crate::protocol::messages::{PlayState, RepeatMode, ShuffleMode};
use crate::protocol::version::ProtocolVersion;
use crate::providers::Providers;
use crate::state::Core;

const POLL_INTERVAL_MS: u64 = 1000;
/// Broadcast `nowplayingposition` every Nth poll tick (so every 20s), matching
/// the shipped C# `TimerConstants.PositionUpdateIntervalMs` (20000). State diffs
/// (shuffle/repeat/scrobble) still check every tick; only position is throttled,
/// since clients advance the seek bar locally between these re-syncs.
const POSITION_EVERY_TICKS: u64 = 20;

/// One tick's frames, per protocol. The V4 and V6 subscriber sets are separate,
/// and a frame shaped for one is unreadable to the other.
#[derive(Default)]
struct Polled {
    v4: Vec<String>,
    v6: Vec<String>,
}

#[derive(Default)]
struct Cached {
    shuffle: Option<ShuffleMode>,
    repeat: Option<RepeatMode>,
    scrobble: Option<bool>,
    stop_after_current: Option<bool>,
}

/// Runs the poll loop until `shutdown` fires.
pub async fn run(core: Arc<Core>, shutdown: Arc<Notify>) {
    let mut interval = tokio::time::interval(Duration::from_millis(POLL_INTERVAL_MS));
    let mut cached = Cached::default();
    let mut tick: u64 = 0;
    loop {
        tokio::select! {
            _ = shutdown.notified() => return,
            _ = interval.tick() => {
                if core.broadcaster.client_count() == 0 && core.v6_broadcaster.client_count() == 0 {
                    continue;
                }
                tick += 1;
                // First position broadcast lands at 20s (tick 20), like the C# timer.
                let emit_position = tick.is_multiple_of(POSITION_EVERY_TICKS);
                let Polled { v4, v6 } = poll(core.providers.as_ref(), &mut cached, &core.now_playing, emit_position);
                core.broadcaster.broadcast(&v4);
                core.v6_broadcaster.broadcast(&v6);
            }
        }
    }
}

/// Queries state and produce the frames that changed since the last tick. The
/// first observation of each diffed value seeds the change-detection cache
/// without broadcasting. The full player state is also written to the shared
/// now-playing cache: the poll is the sole update path for shuffle/repeat/
/// scrobble (MusicBee fires no event for those), so reads stay fresh.
fn poll(
    providers: &dyn Providers,
    cached: &mut Cached,
    store: &NowPlayingCache,
    emit_position: bool,
) -> Polled {
    let mut out = Polled::default();
    let Ok(state) = providers.player_state() else {
        return out;
    };
    store.set_player(state.clone());
    let wire = ProtocolVersion::V4.codec();

    // Position is throttled to every 20s (emit_position); only query it then.
    if emit_position
        && state.play_state == PlayState::Playing
        && let Ok(position) = providers.playback_position()
        && let Ok(value) = serde_json::to_value(&position)
    {
        out.v4.push(frame("nowplayingposition", value));
    }
    if seed_or_changed(&mut cached.shuffle, state.shuffle) {
        out.v4
            .push(frame("playershuffle", wire.shuffle(state.shuffle)));
        out.v6.push(v6::event(
            "shuffle_changed",
            json!({ "shuffle": shuffle_str(state.shuffle) }),
        ));
    }
    if seed_or_changed(&mut cached.repeat, state.repeat) {
        out.v4
            .push(frame("playerrepeat", wire.repeat(state.repeat)));
        out.v6.push(v6::event(
            "repeat_changed",
            json!({ "repeat": repeat_str(state.repeat) }),
        ));
    }
    if seed_or_changed(&mut cached.scrobble, state.scrobble) {
        out.v4.push(frame("scrobbler", json!(state.scrobble)));
        out.v6.push(v6::event(
            "scrobbling_changed",
            json!({ "scrobbling": state.scrobble }),
        ));
    }
    // V6 only: V4 has no spelling for this and is frozen.
    if seed_or_changed(&mut cached.stop_after_current, state.stop_after_current) {
        out.v6.push(v6::event(
            "stop_after_current_changed",
            json!({ "stop_after_current": state.stop_after_current }),
        ));
    }
    out
}

/// Seeds the cache on first observation (no broadcast), then report changes.
fn seed_or_changed<T: PartialEq + Copy>(cached: &mut Option<T>, current: T) -> bool {
    let changed = cached.is_some_and(|c| c != current);
    *cached = Some(current);
    changed
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::PlayerState;
    use crate::providers::MockProviders;
    use serde_json::Value;
    use std::sync::Arc;

    fn store() -> NowPlayingCache {
        // The poll only writes to the store here; its provider side is never
        // read, so a null provider is fine.
        NowPlayingCache::new(Arc::new(crate::providers::NullProviders))
    }

    /// The `event` name of each V6 frame, which is where its identity lives:
    /// V6 has one envelope shape and names the event inside it.
    fn events(frames: &[String]) -> Vec<String> {
        frames
            .iter()
            .map(|f| {
                serde_json::from_str::<Value>(f).unwrap()["event"]
                    .as_str()
                    .unwrap()
                    .to_string()
            })
            .collect()
    }

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

    fn state(
        play: PlayState,
        shuffle: ShuffleMode,
        repeat: RepeatMode,
        scrobble: bool,
    ) -> PlayerState {
        PlayerState {
            play_state: play,
            shuffle,
            repeat,
            scrobble,
            ..Default::default()
        }
    }

    #[test]
    fn first_tick_seeds_diffs_and_broadcasts_position_only_while_playing() {
        let m = MockProviders {
            player_state: state(PlayState::Playing, ShuffleMode::Off, RepeatMode::None, true),
            ..Default::default()
        };
        let mut cached = Cached::default();
        let store = store();
        // On a position tick while playing: position broadcast; shuffle/repeat/
        // scrobble are seeded, not sent.
        assert_eq!(
            contexts(&poll(&m, &mut cached, &store, true).v4),
            vec!["nowplayingposition"]
        );

        // On a non-position tick, position is not sent even while playing.
        assert!(poll(&m, &mut cached, &store, false).v4.is_empty());

        let paused = MockProviders {
            player_state: state(PlayState::Paused, ShuffleMode::Off, RepeatMode::None, true),
            ..Default::default()
        };
        // Position tick but not playing, nothing changed -> no frames.
        assert!(poll(&paused, &mut cached, &store, true).v4.is_empty());
    }

    #[test]
    fn changes_are_broadcast() {
        let mut cached = Cached {
            shuffle: Some(ShuffleMode::Off),
            repeat: Some(RepeatMode::None),
            scrobble: Some(true),
            ..Default::default()
        };
        let m = MockProviders {
            player_state: state(
                PlayState::Paused,
                ShuffleMode::Shuffle,
                RepeatMode::None,
                false,
            ),
            ..Default::default()
        };
        // shuffle off->shuffle and scrobble true->false changed; repeat unchanged.
        assert_eq!(
            contexts(&poll(&m, &mut cached, &store(), false).v4),
            vec!["playershuffle", "scrobbler"]
        );
    }

    #[test]
    fn a_change_reaches_v6_as_well_as_v4() {
        let mut cached = Cached {
            shuffle: Some(ShuffleMode::Off),
            repeat: Some(RepeatMode::All),
            scrobble: Some(false),
            ..Default::default()
        };
        let m = MockProviders {
            player_state: state(
                PlayState::Playing,
                ShuffleMode::AutoDj,
                RepeatMode::One,
                true,
            ),
            ..Default::default()
        };

        let polled = poll(&m, &mut cached, &store(), false);
        assert_eq!(
            events(&polled.v6),
            vec!["shuffle_changed", "repeat_changed", "scrobbling_changed"]
        );
        // The V6 payload carries the new value, so a client needs no follow-up
        // read to know what it changed to.
        let first: Value = serde_json::from_str(&polled.v6[0]).unwrap();
        assert_eq!(first["data"]["shuffle"], "autodj");

        // Both protocols hear the same tick.
        assert_eq!(polled.v4.len(), 3);
    }

    /// MusicBee clears stop-after-current the moment it fires and announces
    /// nothing, so without the poll every client would keep showing a mode that
    /// has already been spent.
    #[test]
    fn the_poll_reports_stop_after_current_clearing_itself() {
        let mut cached = Cached {
            stop_after_current: Some(true),
            ..Default::default()
        };
        let m = MockProviders {
            player_state: PlayerState {
                play_state: PlayState::Stopped,
                stop_after_current: false,
                ..Default::default()
            },
            ..Default::default()
        };

        let polled = poll(&m, &mut cached, &store(), false);
        assert_eq!(events(&polled.v6), vec!["stop_after_current_changed"]);
        let event: Value = serde_json::from_str(&polled.v6[0]).unwrap();
        assert_eq!(event["data"]["stop_after_current"], false);
        // V4 is frozen and has no spelling for it.
        assert!(polled.v4.is_empty());
    }

    #[test]
    fn every_event_the_poll_emits_is_advertised() {
        let mut cached = Cached {
            shuffle: Some(ShuffleMode::Off),
            repeat: Some(RepeatMode::All),
            scrobble: Some(false),
            ..Default::default()
        };
        let m = MockProviders {
            player_state: state(
                PlayState::Playing,
                ShuffleMode::Shuffle,
                RepeatMode::None,
                true,
            ),
            ..Default::default()
        };

        for name in events(&poll(&m, &mut cached, &store(), false).v6) {
            assert!(
                super::super::commands_v6::SUPPORTED_EVENTS.contains(&name.as_str()),
                "{name} is emitted but not advertised in capabilities"
            );
        }
    }
}
