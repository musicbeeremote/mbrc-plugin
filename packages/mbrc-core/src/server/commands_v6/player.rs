//! V6 player-control handlers. The provider calls are identical to the legacy
//! [`commands::player`](super::super::commands::player); only the wire shape
//! differs - typed values, `snake_case` string enums, no toggle, and reads via
//! `player_status` rather than an overloaded null-query (#118).

use serde_json::{json, Value};

use crate::nowplaying::NowPlayingCache;
use crate::protocol::messages::{OutputDevices, PlayState, PlayerState, RepeatMode, ShuffleMode};
use crate::providers::Providers;

use super::{internal, req_bool, req_i64, req_str, OpResult, V6Error};
use mbrc_wire::v6::ErrorCode;

/// The op names this domain serves (advertised in the handshake capabilities).
/// Kept in sync with the `dispatch` match by `every_advertised_op_dispatches`.
pub const OPS: &[&str] = &[
    "player_play",
    "player_pause",
    "player_play_pause",
    "player_stop",
    "player_next",
    "player_previous",
    "player_set_volume",
    "player_set_mute",
    "player_set_shuffle",
    "player_set_repeat",
    "player_set_scrobbling",
    "player_status",
    "player_output",
    "player_set_output",
];

/// Dispatch a `player_*` op. `None` if `op` is not in this domain.
pub fn dispatch(
    op: &str,
    data: &Value,
    p: &dyn Providers,
    now_playing: Option<&NowPlayingCache>,
) -> Option<OpResult> {
    Some(match op {
        "player_play" => transport(p.play()),
        "player_pause" => transport(p.pause()),
        "player_play_pause" => transport(p.play_pause()),
        "player_stop" => transport(p.stop()),
        "player_next" => transport(p.next()),
        "player_previous" => transport(p.previous()),
        "player_set_volume" => set_volume(data, p),
        "player_set_mute" => set_mute(data, p),
        "player_set_shuffle" => set_shuffle(data, p),
        "player_set_repeat" => set_repeat(data, p),
        "player_set_scrobbling" => set_scrobbling(data, p),
        "player_status" => status(p, now_playing),
        "player_output" => output(p),
        "player_set_output" => set_output(data, p),
        _ => return None,
    })
}

/// A fire-and-forget transport action: empty data on success (the
/// `play_state_changed` event delivers the resulting state).
fn transport(r: Result<(), String>) -> OpResult {
    r.map(|_| json!({})).map_err(internal)
}

fn set_volume(data: &Value, p: &dyn Providers) -> OpResult {
    let volume = req_i64(data, "volume")?;
    if !(0..=100).contains(&volume) {
        return Err(V6Error::new(
            ErrorCode::InvalidField,
            "volume must be between 0 and 100",
        ));
    }
    p.set_volume(volume as i32).map_err(internal)?;
    Ok(json!({ "volume": p.player_state().map_err(internal)?.volume }))
}

fn set_mute(data: &Value, p: &dyn Providers) -> OpResult {
    p.set_mute(req_bool(data, "muted")?).map_err(internal)?;
    Ok(json!({ "muted": p.player_state().map_err(internal)?.mute }))
}

/// Set an explicit shuffle mode (no toggle). AutoDJ-aware, mirroring the legacy
/// handler's `set_shuffle` + `set_auto_dj` sequencing.
fn set_shuffle(data: &Value, p: &dyn Providers) -> OpResult {
    match req_str(data, "mode")? {
        "off" => {
            p.set_shuffle(false).map_err(internal)?;
            p.set_auto_dj(false).map_err(internal)?;
        }
        "shuffle" => {
            p.set_auto_dj(false).map_err(internal)?;
            p.set_shuffle(true).map_err(internal)?;
        }
        "autodj" => {
            p.set_shuffle(true).map_err(internal)?;
            p.set_auto_dj(true).map_err(internal)?;
        }
        other => {
            return Err(V6Error::new(
                ErrorCode::InvalidField,
                format!("unknown shuffle mode: {other}"),
            ))
        }
    }
    Ok(json!({ "mode": shuffle_str(p.player_state().map_err(internal)?.shuffle) }))
}

fn set_repeat(data: &Value, p: &dyn Providers) -> OpResult {
    let repeat = match req_str(data, "mode")? {
        "none" => RepeatMode::None,
        "all" => RepeatMode::All,
        "one" => RepeatMode::One,
        other => {
            return Err(V6Error::new(
                ErrorCode::InvalidField,
                format!("unknown repeat mode: {other}"),
            ))
        }
    };
    p.set_repeat(repeat).map_err(internal)?;
    Ok(json!({ "mode": repeat_str(p.player_state().map_err(internal)?.repeat) }))
}

fn set_scrobbling(data: &Value, p: &dyn Providers) -> OpResult {
    let enabled = req_bool(data, "enabled")?;
    // Enabling scrobbling with no last.fm account can't succeed (MusicBee would pop
    // a blocking login modal), so surface it as a proper precondition error rather
    // than a generic internal failure.
    if enabled && !p.has_lastfm_account().map_err(internal)? {
        return Err(V6Error::new(
            ErrorCode::Unavailable,
            "scrobbling requires a configured last.fm account",
        ));
    }
    p.set_scrobble(enabled).map_err(internal)?;
    Ok(json!({ "enabled": p.player_state().map_err(internal)?.scrobble }))
}

/// Full player state (a pure read - served from the now-playing cache when wired).
fn status(p: &dyn Providers, now_playing: Option<&NowPlayingCache>) -> OpResult {
    let state = match now_playing {
        Some(c) => c.player(),
        None => p.player_state().map_err(internal)?,
    };
    Ok(player_status_json(&state))
}

fn output(p: &dyn Providers) -> OpResult {
    Ok(output_json(&p.output_devices().map_err(internal)?))
}

fn set_output(data: &Value, p: &dyn Providers) -> OpResult {
    let device = req_str(data, "device")?;
    if !device.is_empty() {
        p.switch_output(device).map_err(internal)?;
    }
    Ok(output_json(&p.output_devices().map_err(internal)?))
}

// ── canonical player state -> V6 JSON (real types, snake_case enums) ─────────

pub(crate) fn player_status_json(s: &PlayerState) -> Value {
    json!({
        "play_state": play_state_str(s.play_state),
        "volume": s.volume,
        "muted": s.mute,
        "shuffle": shuffle_str(s.shuffle),
        "repeat": repeat_str(s.repeat),
        "scrobbling": s.scrobble,
    })
}

fn output_json(d: &OutputDevices) -> Value {
    json!({ "active": d.active, "devices": d.devices })
}

pub(crate) fn play_state_str(s: PlayState) -> &'static str {
    match s {
        PlayState::Playing => "playing",
        PlayState::Paused => "paused",
        // Stopped and the FFI-default Undefined both surface as "stopped".
        PlayState::Stopped | PlayState::Undefined => "stopped",
    }
}

fn shuffle_str(s: ShuffleMode) -> &'static str {
    match s {
        ShuffleMode::Off => "off",
        ShuffleMode::Shuffle => "shuffle",
        ShuffleMode::AutoDj => "autodj",
    }
}

fn repeat_str(r: RepeatMode) -> &'static str {
    match r {
        RepeatMode::All => "all",
        RepeatMode::One => "one",
        // None and the FFI-default Undefined both surface as "none".
        RepeatMode::None | RepeatMode::Undefined => "none",
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::providers::MockProviders;

    fn mock() -> MockProviders {
        MockProviders {
            player_state: PlayerState {
                play_state: PlayState::Playing,
                volume: 42,
                mute: false,
                shuffle: ShuffleMode::Off,
                repeat: RepeatMode::None,
                position: 0,
                scrobble: true,
            },
            ..Default::default()
        }
    }

    fn run(op: &str, data: Value, m: &MockProviders) -> OpResult {
        dispatch(op, &data, m, None).expect("player op should be recognized")
    }

    #[test]
    fn unknown_op_is_not_in_this_domain() {
        let m = mock();
        assert!(dispatch("library_browse", &json!({}), &m, None).is_none());
    }

    #[test]
    fn every_advertised_op_dispatches() {
        // The advertised OPS list must match the dispatch match (no drift): every
        // advertised op is recognized (Some), even if it errors on missing data.
        let m = mock();
        for op in OPS {
            assert!(
                dispatch(op, &json!({}), &m, None).is_some(),
                "advertised op {op} is not dispatched"
            );
        }
    }

    #[test]
    fn transport_acks_empty_and_calls_provider() {
        let m = mock();
        assert_eq!(run("player_next", json!({}), &m).unwrap(), json!({}));
        assert!(m.recorded().contains(&"next".to_string()));
    }

    #[test]
    fn set_volume_sets_then_reads_back() {
        let m = mock();
        let out = run("player_set_volume", json!({ "volume": 80 }), &m).unwrap();
        assert_eq!(out, json!({ "volume": 42 })); // reads back state
        assert!(m.recorded().contains(&"set_volume(80)".to_string()));
    }

    #[test]
    fn set_volume_out_of_range_is_invalid_field() {
        let m = mock();
        let err = run("player_set_volume", json!({ "volume": 150 }), &m).unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
    }

    #[test]
    fn set_volume_missing_field() {
        let m = mock();
        let err = run("player_set_volume", json!({}), &m).unwrap_err();
        assert_eq!(err.code, ErrorCode::MissingField);
    }

    #[test]
    fn set_shuffle_autodj_sets_both_and_replies_mode() {
        let mut m = mock();
        m.player_state.shuffle = ShuffleMode::AutoDj;
        let out = run("player_set_shuffle", json!({ "mode": "autodj" }), &m).unwrap();
        assert_eq!(out, json!({ "mode": "autodj" }));
        let calls = m.recorded();
        assert!(calls.contains(&"set_shuffle(true)".to_string()));
        assert!(calls.contains(&"set_auto_dj(true)".to_string()));
    }

    #[test]
    fn set_shuffle_unknown_mode_is_invalid_field() {
        let m = mock();
        let err = run("player_set_shuffle", json!({ "mode": "sideways" }), &m).unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
    }

    #[test]
    fn enabling_scrobbling_without_lastfm_is_unavailable() {
        let m = mock(); // has_lastfm_account defaults false
        let err = run("player_set_scrobbling", json!({ "enabled": true }), &m).unwrap_err();
        assert_eq!(err.code, ErrorCode::Unavailable);
        // The set command was never attempted.
        assert!(!m.recorded().iter().any(|c| c.starts_with("set_scrobble")));
    }

    #[test]
    fn enabling_scrobbling_with_lastfm_proceeds() {
        let m = MockProviders {
            has_lastfm_account: true,
            ..mock()
        };
        run("player_set_scrobbling", json!({ "enabled": true }), &m).unwrap();
        assert!(m.recorded().iter().any(|c| c.starts_with("set_scrobble")));
    }

    #[test]
    fn disabling_scrobbling_skips_the_lastfm_check() {
        let m = mock(); // no account, but disabling is always allowed
        run("player_set_scrobbling", json!({ "enabled": false }), &m).unwrap();
        assert!(m.recorded().iter().any(|c| c.starts_with("set_scrobble")));
    }

    #[test]
    fn set_repeat_maps_and_reads_back() {
        let mut m = mock();
        m.player_state.repeat = RepeatMode::All;
        let out = run("player_set_repeat", json!({ "mode": "all" }), &m).unwrap();
        assert_eq!(out, json!({ "mode": "all" }));
        assert!(m.recorded().contains(&"set_repeat(All)".to_string()));
    }

    #[test]
    fn status_is_typed() {
        let m = mock();
        let out = run("player_status", json!({}), &m).unwrap();
        assert_eq!(
            out,
            json!({
                "play_state": "playing",
                "volume": 42,
                "muted": false,
                "shuffle": "off",
                "repeat": "none",
                "scrobbling": true,
            })
        );
    }

    #[test]
    fn set_output_switches_and_reads_back() {
        let m = MockProviders {
            output_devices: OutputDevices {
                active: "Speakers".into(),
                devices: vec!["Speakers".into(), "Headphones".into()],
            },
            ..mock()
        };
        let out = run("player_set_output", json!({ "device": "Headphones" }), &m).unwrap();
        assert_eq!(out["active"], "Speakers"); // mock reads back its canned state
        assert!(m
            .recorded()
            .contains(&"switch_output(Headphones)".to_string()));
    }
}
