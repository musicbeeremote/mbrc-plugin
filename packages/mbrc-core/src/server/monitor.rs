//! Poll-driven broadcasts (the C# `StateMonitor` port). Some state changes fire
//! no MusicBee event: playback position advances continuously, and a user can
//! change shuffle/repeat/scrobble in MusicBee's own UI. A timer task polls the
//! provider RPC, broadcasts `nowplayingposition` while playing, and broadcasts
//! shuffle/repeat/scrobble only when they change.
//!
//! Only polls while at least one client is connected, so an idle core makes no
//! FFI calls.

use std::sync::Arc;
use std::time::Duration;

use serde_json::json;
use tokio::sync::Notify;

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

#[derive(Default)]
struct Cached {
    shuffle: Option<ShuffleMode>,
    repeat: Option<RepeatMode>,
    scrobble: Option<bool>,
}

/// Run the poll loop until `shutdown` fires.
pub async fn run(core: Arc<Core>, shutdown: Arc<Notify>) {
    let mut interval = tokio::time::interval(Duration::from_millis(POLL_INTERVAL_MS));
    let mut cached = Cached::default();
    let mut tick: u64 = 0;
    loop {
        tokio::select! {
            _ = shutdown.notified() => return,
            _ = interval.tick() => {
                if core.broadcaster.client_count() == 0 {
                    continue;
                }
                tick += 1;
                // First position broadcast lands at 20s (tick 20), like the C# timer.
                let emit_position = tick.is_multiple_of(POSITION_EVERY_TICKS);
                let frames = poll(core.providers.as_ref(), &mut cached, &core.now_playing, emit_position);
                core.broadcaster.broadcast(&frames);
            }
        }
    }
}

/// Query state and produce the frames that changed since the last tick. The
/// first observation of each diffed value seeds the change-detection cache
/// without broadcasting. The full player state is also written to the shared
/// now-playing cache: the poll is the sole update path for shuffle/repeat/
/// scrobble (MusicBee fires no event for those), so reads stay fresh.
fn poll(
    providers: &dyn Providers,
    cached: &mut Cached,
    store: &NowPlayingCache,
    emit_position: bool,
) -> Vec<String> {
    let mut frames = Vec::new();
    let Ok(state) = providers.player_state() else {
        return frames;
    };
    store.set_player(state.clone());
    // Poll broadcasts are V4-formatted; per-client version fan-out is a V6 concern.
    let wire = ProtocolVersion::V4.codec();

    // Position is throttled to every 20s (emit_position); only query it then.
    if emit_position && state.play_state == PlayState::Playing {
        if let Ok(position) = providers.playback_position() {
            if let Ok(value) = serde_json::to_value(&position) {
                frames.push(frame("nowplayingposition", value));
            }
        }
    }
    if seed_or_changed(&mut cached.shuffle, state.shuffle) {
        frames.push(frame("playershuffle", wire.shuffle(state.shuffle)));
    }
    if seed_or_changed(&mut cached.repeat, state.repeat) {
        frames.push(frame("playerrepeat", wire.repeat(state.repeat)));
    }
    if seed_or_changed(&mut cached.scrobble, state.scrobble) {
        frames.push(frame("scrobbler", json!(state.scrobble)));
    }
    frames
}

/// Seed the cache on first observation (no broadcast), then report changes.
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
            contexts(&poll(&m, &mut cached, &store, true)),
            vec!["nowplayingposition"]
        );

        // On a non-position tick, position is not sent even while playing.
        assert!(poll(&m, &mut cached, &store, false).is_empty());

        let paused = MockProviders {
            player_state: state(PlayState::Paused, ShuffleMode::Off, RepeatMode::None, true),
            ..Default::default()
        };
        // Position tick but not playing, nothing changed -> no frames.
        assert!(poll(&paused, &mut cached, &store, true).is_empty());
    }

    #[test]
    fn changes_are_broadcast() {
        let mut cached = Cached {
            shuffle: Some(ShuffleMode::Off),
            repeat: Some(RepeatMode::None),
            scrobble: Some(true),
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
            contexts(&poll(&m, &mut cached, &store(), false)),
            vec!["playershuffle", "scrobbler"]
        );
    }
}
