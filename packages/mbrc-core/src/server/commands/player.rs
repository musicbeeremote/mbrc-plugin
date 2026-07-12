//! Player-control handlers. Transport commands echo `true`; getters/setters
//! reply the current value read back from `player_state`. Handlers hold the
//! strictly-typed canonical state; the V4 wire spelling (stringified volume,
//! enum spellings) is applied by `ctx.wire()` (the `wire::v4` formatter).

use serde_json::{json, Value};

use super::{as_bool_lenient, as_int_lenient, Ctx, HandlerResult};
use crate::protocol::messages::{RepeatMode, ShuffleMode};

/// One reply on the same context, echoing `true` for a fire-and-forget action.
fn ack(context: &str) -> HandlerResult {
    Ok(vec![(context.to_string(), json!(true))])
}

pub fn play(ctx: &Ctx) -> HandlerResult {
    ctx.providers.play()?;
    ack("playerplay")
}

pub fn pause(ctx: &Ctx) -> HandlerResult {
    ctx.providers.pause()?;
    ack("playerpause")
}

pub fn play_pause(ctx: &Ctx) -> HandlerResult {
    ctx.providers.play_pause()?;
    ack("playerplaypause")
}

pub fn stop(ctx: &Ctx) -> HandlerResult {
    ctx.providers.stop()?;
    ack("playerstop")
}

pub fn next(ctx: &Ctx) -> HandlerResult {
    ctx.providers.next()?;
    ack("playernext")
}

pub fn previous(ctx: &Ctx) -> HandlerResult {
    ctx.providers.previous()?;
    ack("playerprevious")
}

/// Set (int, or a stringified int) or query (null); always reply the current
/// volume as an int.
pub fn volume(data: &Value, ctx: &Ctx) -> HandlerResult {
    let p = ctx.providers;
    if let Some(v) = as_int_lenient(data) {
        p.set_volume(v as i32)?;
    }
    let volume = p.player_state()?.volume;
    Ok(vec![("playervolume".to_string(), json!(volume))])
}

/// Set (bool, or `"true"`/`1`), toggle (`"toggle"`), or query (null); reply the
/// current mute.
pub fn mute(data: &Value, ctx: &Ctx) -> HandlerResult {
    let p = ctx.providers;
    if data.as_str() == Some("toggle") {
        let current = p.player_state()?.mute;
        p.set_mute(!current)?;
    } else if let Some(b) = as_bool_lenient(data) {
        p.set_mute(b)?;
    }
    let mute = p.player_state()?.mute;
    Ok(vec![("playermute".to_string(), json!(mute))])
}

/// Set (`"off"`/`"shuffle"`/`"autodj"`), toggle, or query; reply the current
/// shuffle state. AutoDJ-aware, mirroring the shipped C# `HandleAutoDjShuffle`
/// (every V4 client negotiates AutoDJ shuffle): the toggle cycles
/// Off -> Shuffle -> AutoDj -> Off, and AutoDJ is a real settable state.
pub fn shuffle(data: &Value, ctx: &Ctx) -> HandlerResult {
    let p = ctx.providers;
    match data.as_str() {
        Some("toggle") => match p.player_state()?.shuffle {
            ShuffleMode::Off => p.set_shuffle(true)?,
            ShuffleMode::Shuffle => p.set_auto_dj(true)?,
            ShuffleMode::AutoDj => p.set_auto_dj(false)?,
        },
        // Explicit target states mirror C# SetAutoDjState.
        Some("autodj") => {
            p.set_shuffle(true)?;
            p.set_auto_dj(true)?;
        }
        Some("shuffle") => {
            p.set_auto_dj(false)?;
            p.set_shuffle(true)?;
        }
        Some("off") => {
            p.set_shuffle(false)?;
            p.set_auto_dj(false)?;
        }
        _ => {}
    }
    let shuffle = p.player_state()?.shuffle;
    Ok(vec![(
        "playershuffle".to_string(),
        ctx.wire().shuffle(shuffle),
    )])
}

/// Set (`"None"`/`"All"`/`"One"`), toggle (cycles), or query; reply the current
/// repeat mode.
pub fn repeat(data: &Value, ctx: &Ctx) -> HandlerResult {
    let p = ctx.providers;
    match data.as_str() {
        Some("toggle") => {
            let next = match p.player_state()?.repeat {
                RepeatMode::None => RepeatMode::All,
                RepeatMode::All => RepeatMode::One,
                _ => RepeatMode::None,
            };
            p.set_repeat(next)?;
        }
        // An unrecognized/empty value parses to None -> query (no set), matching C#.
        Some(mode) => {
            if let Some(repeat) = ctx.wire().parse_repeat(mode) {
                p.set_repeat(repeat)?;
            }
        }
        _ => {}
    }
    let repeat = p.player_state()?.repeat;
    Ok(vec![(
        "playerrepeat".to_string(),
        ctx.wire().repeat(repeat),
    )])
}

/// Set (bool, or `"true"`/`1`), toggle, or query; reply the current scrobbler
/// state as a bool.
pub fn scrobble(data: &Value, ctx: &Ctx) -> HandlerResult {
    let p = ctx.providers;
    if data.as_str() == Some("toggle") {
        let current = p.player_state()?.scrobble;
        p.set_scrobble(!current)?;
    } else if let Some(b) = as_bool_lenient(data) {
        p.set_scrobble(b)?;
    }
    let scrobble = p.player_state()?.scrobble;
    Ok(vec![("scrobbler".to_string(), json!(scrobble))])
}

/// Reply the full `playerstatus` object (volume stringified per V4).
pub fn status(ctx: &Ctx) -> HandlerResult {
    // Pure query -> served from the now-playing cache (no FFI).
    let state = ctx.now_player()?;
    Ok(vec![(
        "playerstatus".to_string(),
        ctx.wire().player_status(&state),
    )])
}

/// `playeroutput`: pure query of the device list. The client sends `data: ""`
/// here, so this must NOT switch (the shipped `HandleOutputDevices` doesn't) -
/// switching lives in `output_switch`.
pub fn output(_data: &Value, ctx: &Ctx) -> HandlerResult {
    let devices = ctx.providers.output_devices()?;
    Ok(vec![(
        "playeroutput".to_string(),
        ctx.wire().output_devices(&devices),
    )])
}

/// `playeroutputswitch`: switch to the named device (ignoring an empty name),
/// then reply the updated device list on the `playeroutput` context (matching
/// the shipped `HandleOutputDeviceSwitch`).
pub fn output_switch(data: &Value, ctx: &Ctx) -> HandlerResult {
    if let Some(device) = data.as_str() {
        if !device.is_empty() {
            ctx.providers.switch_output(device)?;
        }
    }
    let devices = ctx.providers.output_devices()?;
    Ok(vec![(
        "playeroutput".to_string(),
        ctx.wire().output_devices(&devices),
    )])
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{PlayState, PlayerState};
    use crate::protocol::version::ProtocolVersion;
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

    fn ctx(m: &MockProviders) -> Ctx<'_> {
        Ctx::new(m, ProtocolVersion::V4)
    }

    #[test]
    fn transport_acks_true_and_calls_provider() {
        let m = mock();
        assert_eq!(
            play_pause(&ctx(&m)).unwrap(),
            vec![("playerplaypause".into(), json!(true))]
        );
        assert_eq!(
            next(&ctx(&m)).unwrap(),
            vec![("playernext".into(), json!(true))]
        );
        assert!(m.recorded().contains(&"play_pause".to_string()));
        assert!(m.recorded().contains(&"next".to_string()));
    }

    #[test]
    fn volume_sets_then_replies_int() {
        let m = mock();
        let out = volume(&json!(80), &ctx(&m)).unwrap();
        assert_eq!(out, vec![("playervolume".into(), json!(42))]); // reads back state
        assert!(m.recorded().contains(&"set_volume(80)".to_string()));
    }

    #[test]
    fn shuffle_query_replies_wire_string() {
        let m = mock();
        let out = shuffle(&Value::Null, &ctx(&m)).unwrap();
        assert_eq!(out, vec![("playershuffle".into(), json!("off"))]);
    }

    #[test]
    fn shuffle_toggle_cycles_off_shuffle_autodj() {
        // Off -> turn shuffle on.
        let mut m = mock();
        m.player_state.shuffle = ShuffleMode::Off;
        shuffle(&json!("toggle"), &ctx(&m)).unwrap();
        assert!(m.recorded().contains(&"set_shuffle(true)".to_string()));

        // Shuffle -> turn AutoDJ on.
        let mut m = mock();
        m.player_state.shuffle = ShuffleMode::Shuffle;
        shuffle(&json!("toggle"), &ctx(&m)).unwrap();
        assert!(m.recorded().contains(&"set_auto_dj(true)".to_string()));

        // AutoDj -> turn AutoDJ off.
        let mut m = mock();
        m.player_state.shuffle = ShuffleMode::AutoDj;
        shuffle(&json!("toggle"), &ctx(&m)).unwrap();
        assert!(m.recorded().contains(&"set_auto_dj(false)".to_string()));
    }

    #[test]
    fn output_query_does_not_switch_but_switch_does() {
        // `playeroutput` query (empty-string data) must not switch devices.
        let m = mock();
        let out = output(&json!(""), &ctx(&m)).unwrap();
        assert_eq!(out[0].0, "playeroutput");
        assert!(!m.recorded().iter().any(|c| c.starts_with("switch_output")));

        // `playeroutputswitch` with a name switches; empty name is ignored.
        let m = mock();
        output_switch(&json!("Headphones"), &ctx(&m)).unwrap();
        assert!(m
            .recorded()
            .contains(&"switch_output(Headphones)".to_string()));

        let m = mock();
        output_switch(&json!(""), &ctx(&m)).unwrap();
        assert!(!m.recorded().iter().any(|c| c.starts_with("switch_output")));
    }

    #[test]
    fn shuffle_explicit_autodj_sets_both() {
        let m = mock();
        shuffle(&json!("autodj"), &ctx(&m)).unwrap();
        let calls = m.recorded();
        assert!(calls.contains(&"set_shuffle(true)".to_string()));
        assert!(calls.contains(&"set_auto_dj(true)".to_string()));
    }

    #[test]
    fn playerstatus_uses_legacy_shape() {
        let m = mock();
        let out = status(&ctx(&m)).unwrap();
        assert_eq!(out[0].0, "playerstatus");
        assert_eq!(out[0].1["playervolume"], json!("42")); // stringified
        assert_eq!(out[0].1["playershuffle"], json!("off"));
        assert_eq!(out[0].1["scrobbler"], json!(true));
    }

    #[test]
    fn repeat_toggle_cycles_none_to_all() {
        let m = mock();
        let out = repeat(&json!("toggle"), &ctx(&m)).unwrap();
        assert!(m.recorded().contains(&"set_repeat(All)".to_string()));
        assert_eq!(out[0], ("playerrepeat".into(), json!("None"))); // reads back state
    }
}
