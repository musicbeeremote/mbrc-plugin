//! V6 now-playing STATE domain: the current track and its facets. A composite
//! `now_playing_state` read plus extended details, position, structured lyrics
//! (#113), and the granular writes (seek / rating / last.fm / tag edit).
//!
//! The now-playing LIST (`now_playing_list` + play/remove/move/search/queue) is a
//! separate sub-step.

use serde_json::{json, Value};

use mbrc_wire::v6::ErrorCode;

use super::{i32_saturating, internal, req_i64, req_str, track, OpResult, V6Error};
use crate::cover::store::CoverStore;
use crate::nowplaying::NowPlayingCache;
use crate::protocol::messages::{LastfmStatus, TrackDetails};
use crate::providers::Providers;

/// The op names this domain serves (advertised in the handshake capabilities).
pub const OPS: &[&str] = &[
    "now_playing_state",
    "now_playing_details",
    "now_playing_position",
    "now_playing_lyrics",
    "now_playing_seek",
    "now_playing_set_rating",
    "now_playing_set_lfm",
    "now_playing_set_tag",
];

/// Dispatch a `now_playing_*` state op. `None` if `op` is not in this domain.
pub fn dispatch(
    op: &str,
    data: &Value,
    p: &dyn Providers,
    now_playing: Option<&NowPlayingCache>,
    cover_store: Option<&CoverStore>,
) -> Option<OpResult> {
    Some(match op {
        "now_playing_state" => state(p, now_playing, cover_store),
        "now_playing_details" => details(p, now_playing),
        "now_playing_position" => position(p),
        "now_playing_lyrics" => lyrics(p),
        "now_playing_seek" => seek(data, p),
        "now_playing_set_rating" => set_rating(data, p),
        "now_playing_set_lfm" => set_lfm(data, p),
        "now_playing_set_tag" => set_tag(data, p),
        _ => return None,
    })
}

/// Composite now-playing state: the canonical current track (or `null`), the
/// position, and the last.fm status. `rating` lives on `track.rating`.
fn state(
    p: &dyn Providers,
    now_playing: Option<&NowPlayingCache>,
    store: Option<&CoverStore>,
) -> OpResult {
    let (path, lfm) = match now_playing {
        Some(c) => (c.track_info().path, c.lfm()),
        None => (
            p.track_info().map_err(internal)?.path,
            p.lfm_rating().map_err(internal)?,
        ),
    };
    let track = if path.is_empty() {
        Value::Null
    } else {
        match p
            .tracks_detailed_for_paths(vec![path])
            .map_err(internal)?
            .into_iter()
            .next()
        {
            Some(tags) => track::track_json(&tags, track::cover_hash_for(store, &tags).as_deref()),
            None => Value::Null,
        }
    };
    let pos = p.playback_position().map_err(internal)?;
    Ok(json!({
        "track": track,
        "position_ms": pos.current,
        "duration_ms": pos.total,
        "lfm_status": lfm_str(lfm),
    }))
}

/// Extended metadata not carried on the canonical track. Numeric fields parse to
/// int (`null` when unparseable); the rest pass through as MusicBee's strings.
fn details(p: &dyn Providers, now_playing: Option<&NowPlayingCache>) -> OpResult {
    let d = match now_playing {
        Some(c) => c.track_details(),
        None => p.track_details().map_err(internal)?,
    };
    Ok(details_json(&d))
}

fn details_json(d: &TrackDetails) -> Value {
    let int = |s: &str| s.trim().parse::<i64>().ok();
    json!({
        "track_count": int(&d.track_count),
        "disc_count": int(&d.disc_count),
        "play_count": int(&d.play_count),
        "skip_count": int(&d.skip_count),
        "channels": int(&d.channels),
        "sample_rate": int(&d.sample_rate),
        "bitrate": int(&d.bitrate),
        "publisher": d.publisher.as_str(),
        "composer": d.composer.as_str(),
        "comment": d.comment.as_str(),
        "grouping": d.grouping.as_str(),
        "rating_album": d.rating_album.as_str(),
        "encoder": d.encoder.as_str(),
        "kind": d.kind.as_str(),
        "format": d.format.as_str(),
        "size": d.size.as_str(),
        "date_modified": d.date_modified.as_str(),
        "last_played": d.last_played.as_str(),
    })
}

fn position(p: &dyn Providers) -> OpResult {
    let pos = p.playback_position().map_err(internal)?;
    Ok(json!({ "position_ms": pos.current, "duration_ms": pos.total }))
}

fn lyrics(p: &dyn Providers) -> OpResult {
    let raw = p.now_playing_synced_lyrics().map_err(internal)?;
    Ok(parse_lyrics(&raw.lyrics))
}

fn seek(data: &Value, p: &dyn Providers) -> OpResult {
    let ms = req_i64(data, "position_ms")?;
    if ms < 0 {
        return Err(V6Error::new(
            ErrorCode::InvalidField,
            "position_ms must be >= 0",
        ));
    }
    p.set_position(i32_saturating(ms)).map_err(internal)?;
    let pos = p.playback_position().map_err(internal)?;
    Ok(json!({ "position_ms": pos.current, "duration_ms": pos.total }))
}

/// Set the current track's rating (0-5, or `null` to clear). Returns the read-back
/// rating as a float (or `null`).
fn set_rating(data: &Value, p: &dyn Providers) -> OpResult {
    let value = match data.get("rating") {
        None => {
            return Err(V6Error::new(
                ErrorCode::MissingField,
                "missing required field: rating",
            ))
        }
        Some(Value::Null) => String::new(), // clear
        Some(v) => {
            let r = v
                .as_f64()
                .ok_or_else(|| V6Error::new(ErrorCode::InvalidField, "rating must be a number"))?;
            if !(0.0..=5.0).contains(&r) {
                return Err(V6Error::new(ErrorCode::InvalidField, "rating must be 0-5"));
            }
            format_rating(r)
        }
    };
    p.set_rating(&value).map_err(internal)?;
    let back = p.rating().map_err(internal)?;
    Ok(json!({ "rating": track::parse_rating(&back) }))
}

fn set_lfm(data: &Value, p: &dyn Providers) -> OpResult {
    let status = match req_str(data, "status")? {
        "normal" => LastfmStatus::Normal,
        "love" => LastfmStatus::Love,
        "ban" => LastfmStatus::Ban,
        other => {
            return Err(V6Error::new(
                ErrorCode::InvalidField,
                format!("unknown lfm status: {other}"),
            ))
        }
    };
    p.set_lfm_rating(status).map_err(internal)?;
    Ok(json!({ "lfm_status": lfm_str(p.lfm_rating().map_err(internal)?) }))
}

fn set_tag(data: &Value, p: &dyn Providers) -> OpResult {
    let tag = req_str(data, "tag")?;
    if tag.is_empty() {
        return Err(V6Error::new(
            ErrorCode::InvalidField,
            "tag must not be empty",
        ));
    }
    let value = req_str(data, "value")?;
    p.set_tag(tag, value).map_err(internal)?;
    Ok(json!({}))
}

// ── helpers ─────────────────────────────────────────────────────────────────

pub(crate) fn lfm_str(status: LastfmStatus) -> &'static str {
    match status {
        LastfmStatus::Normal => "normal",
        LastfmStatus::Love => "love",
        LastfmStatus::Ban => "ban",
    }
}

/// Format a 0-5 rating for the provider: an integer stays integer (`4`), otherwise
/// a decimal (`3.5`).
fn format_rating(r: f64) -> String {
    if r.fract() == 0.0 {
        format!("{}", r as i64)
    } else {
        format!("{r}")
    }
}

/// Parse lyrics (possibly LRC-timestamped) into structured V6 lines:
/// `{ type: "synced"|"plain"|"none", lines: [{ text, at_ms? }] }`.
pub(crate) fn parse_lyrics(raw: &str) -> Value {
    let text = raw.trim();
    if text.is_empty() {
        return json!({ "type": "none" });
    }
    let mut lines = Vec::new();
    let mut any_synced = false;
    for line in text.lines() {
        let (at_ms, content) = strip_lrc_prefix(line);
        if at_ms.is_some() {
            any_synced = true;
        }
        // Skip empty lines and pure LRC metadata tags (`[ti:...]`, `[ar:...]`).
        if at_ms.is_none() && (content.is_empty() || is_metadata_tag(content)) {
            continue;
        }
        let mut obj = json!({ "text": content });
        if let Some(ms) = at_ms {
            obj["at_ms"] = json!(ms);
        }
        lines.push(obj);
    }
    if lines.is_empty() {
        return json!({ "type": "none" });
    }
    json!({
        "type": if any_synced { "synced" } else { "plain" },
        "lines": lines,
    })
}

/// Strip leading LRC time tags from a line, returning the first tag's ms (if any)
/// and the remaining text.
fn strip_lrc_prefix(line: &str) -> (Option<i64>, &str) {
    let mut rest = line.trim_start();
    let mut first_ms = None;
    while let Some(stripped) = rest.strip_prefix('[') {
        let Some(end) = stripped.find(']') else { break };
        match lrc_time_ms(&stripped[..end]) {
            Some(ms) => {
                first_ms.get_or_insert(ms);
                rest = stripped[end + 1..].trim_start();
            }
            None => break, // a non-time tag (metadata / lyric text) - leave it
        }
    }
    (first_ms, rest.trim())
}

/// Parse an LRC time tag `mm:ss`, `mm:ss.xx`, or `mm:ss.xxx` to milliseconds.
fn lrc_time_ms(tag: &str) -> Option<i64> {
    let (mm, rest) = tag.split_once(':')?;
    let minutes: i64 = mm.trim().parse().ok()?;
    if minutes < 0 {
        return None;
    }
    let (sec, frac) = match rest.find(['.', ':']) {
        Some(i) => (&rest[..i], &rest[i + 1..]),
        None => (rest, ""),
    };
    let seconds: i64 = sec.trim().parse().ok()?;
    if !(0..60).contains(&seconds) {
        return None;
    }
    let frac_ms = if frac.is_empty() {
        0
    } else {
        let digits: String = frac
            .chars()
            .take_while(char::is_ascii_digit)
            .take(3)
            .collect();
        if digits.is_empty() {
            return None;
        }
        let val: i64 = digits.parse().ok()?;
        match digits.len() {
            1 => val * 100,
            2 => val * 10,
            _ => val,
        }
    };
    // Checked arithmetic (like parse_duration_ms): a pathological timestamp such
    // as `[9999999999999:00]` overflows i64 - yield None rather than a wrapped
    // negative `at_ms`.
    minutes
        .checked_mul(60)?
        .checked_add(seconds)?
        .checked_mul(1000)?
        .checked_add(frac_ms)
}

/// A whole-line LRC metadata tag like `[ti:Song]` / `[ar:Artist]` (a known key
/// before the colon), which is not lyric text.
fn is_metadata_tag(line: &str) -> bool {
    const KEYS: &[&str] = &[
        "ti", "ar", "al", "au", "by", "re", "ve", "offset", "length", "la", "lang", "tool", "id",
    ];
    let inner = match line.strip_prefix('[').and_then(|s| s.strip_suffix(']')) {
        Some(inner) => inner,
        None => return false,
    };
    match inner.split_once(':') {
        Some((key, _)) => KEYS.contains(&key.trim().to_ascii_lowercase().as_str()),
        None => false,
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use crate::protocol::messages::{Lyrics, PlaybackPositionResponse, TrackInfo, TrackTags};
    use crate::providers::MockProviders;

    fn run(op: &str, data: Value, m: &MockProviders) -> OpResult {
        dispatch(op, &data, m, None, None).expect("now_playing op should be recognized")
    }

    #[test]
    fn parse_lyrics_synced_plain_and_none() {
        let synced = parse_lyrics("[00:12.34] hello\n[00:15.90]world");
        assert_eq!(synced["type"], "synced");
        assert_eq!(synced["lines"][0]["at_ms"], 12_340);
        assert_eq!(synced["lines"][0]["text"], "hello");
        assert_eq!(synced["lines"][1]["at_ms"], 15_900);

        let plain = parse_lyrics("line one\nline two");
        assert_eq!(plain["type"], "plain");
        assert_eq!(plain["lines"][1]["text"], "line two");
        assert!(plain["lines"][0].get("at_ms").is_none());

        assert_eq!(parse_lyrics("")["type"], "none");
        assert_eq!(parse_lyrics("   \n  ")["type"], "none");
    }

    #[test]
    fn parse_lyrics_skips_metadata_tags() {
        let v = parse_lyrics("[ti:Song][ar:Artist]\n[00:01.00] real line");
        assert_eq!(v["type"], "synced");
        assert_eq!(v["lines"].as_array().unwrap().len(), 1);
        assert_eq!(v["lines"][0]["text"], "real line");
    }

    #[test]
    fn state_builds_typed_track_position_lfm() {
        let m = MockProviders {
            track_info: TrackInfo {
                path: "a.mp3".into(),
                ..Default::default()
            },
            tracks_detailed: vec![TrackTags {
                src: "a.mp3".into(),
                title: "A".into(),
                duration: "3:00".into(),
                ..Default::default()
            }],
            position: PlaybackPositionResponse {
                current: 42_000,
                total: 180_000,
            },
            lfm_rating: LastfmStatus::Love,
            ..Default::default()
        };
        let out = run("now_playing_state", json!({}), &m).unwrap();
        assert_eq!(out["track"]["title"], "A");
        assert_eq!(out["track"]["duration_ms"], 180_000);
        assert_eq!(out["position_ms"], 42_000);
        assert_eq!(out["duration_ms"], 180_000);
        assert_eq!(out["lfm_status"], "love");
    }

    #[test]
    fn state_track_is_null_when_nothing_playing() {
        let m = MockProviders::default(); // empty path
        let out = run("now_playing_state", json!({}), &m).unwrap();
        assert!(out["track"].is_null());
    }

    #[test]
    fn details_parses_numeric_fields() {
        let m = MockProviders {
            track_details: TrackDetails {
                play_count: "17".into(),
                bitrate: "320".into(),
                publisher: "Label".into(),
                ..Default::default()
            },
            ..Default::default()
        };
        let out = run("now_playing_details", json!({}), &m).unwrap();
        assert_eq!(out["play_count"], 17);
        assert_eq!(out["bitrate"], 320);
        assert_eq!(out["publisher"], "Label");
    }

    #[test]
    fn seek_sets_and_reads_back() {
        let m = MockProviders {
            position: PlaybackPositionResponse {
                current: 5_000,
                total: 200_000,
            },
            ..Default::default()
        };
        let out = run("now_playing_seek", json!({ "position_ms": 5000 }), &m).unwrap();
        assert_eq!(out["position_ms"], 5_000);
        assert!(m.recorded().contains(&"set_position(5000)".to_string()));
    }

    #[test]
    fn set_rating_formats_and_clears() {
        let m = MockProviders::default();
        run("now_playing_set_rating", json!({ "rating": 4.0 }), &m).unwrap();
        assert!(m.recorded().contains(&"set_rating(4)".to_string()));
        let m2 = MockProviders::default();
        run("now_playing_set_rating", json!({ "rating": 3.5 }), &m2).unwrap();
        assert!(m2.recorded().contains(&"set_rating(3.5)".to_string()));
        let m3 = MockProviders::default();
        run(
            "now_playing_set_rating",
            json!({ "rating": Value::Null }),
            &m3,
        )
        .unwrap();
        assert!(m3.recorded().contains(&"set_rating()".to_string()));
    }

    #[test]
    fn set_rating_out_of_range_is_invalid_field() {
        let m = MockProviders::default();
        let err = run("now_playing_set_rating", json!({ "rating": 9 }), &m).unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
    }

    #[test]
    fn set_lfm_maps_status_and_rejects_bad() {
        // Love/ban write the RatingLove track tag and need no last.fm account,
        // so this succeeds even with the default (no-account) mock.
        let m = MockProviders {
            lfm_rating: LastfmStatus::Love,
            ..Default::default()
        };
        let out = run("now_playing_set_lfm", json!({ "status": "love" }), &m).unwrap();
        assert_eq!(out["lfm_status"], "love");
        assert!(m.recorded().contains(&"set_lfm_rating(Love)".to_string()));

        let bad = run("now_playing_set_lfm", json!({ "status": "meh" }), &m).unwrap_err();
        assert_eq!(bad.code, ErrorCode::InvalidField);
    }

    #[test]
    fn set_tag_edits_and_rejects_empty_tag() {
        let m = MockProviders::default();
        run(
            "now_playing_set_tag",
            json!({ "tag": "artist", "value": "New" }),
            &m,
        )
        .unwrap();
        assert!(m.recorded().contains(&"set_tag(artist,New)".to_string()));

        let err = run(
            "now_playing_set_tag",
            json!({ "tag": "", "value": "x" }),
            &m,
        )
        .unwrap_err();
        assert_eq!(err.code, ErrorCode::InvalidField);
    }

    #[test]
    fn lyrics_op_parses_provider_text() {
        let m = MockProviders {
            lyrics: Lyrics {
                status: 200,
                lyrics: "[00:01.00] hi".into(),
            },
            ..Default::default()
        };
        let out = run("now_playing_lyrics", json!({}), &m).unwrap();
        assert_eq!(out["type"], "synced");
        assert_eq!(out["lines"][0]["text"], "hi");
    }

    #[test]
    fn unknown_op_is_not_in_this_domain() {
        let m = MockProviders::default();
        assert!(dispatch("player_status", &json!({}), &m, None, None).is_none());
    }

    #[test]
    fn every_advertised_op_dispatches() {
        let m = MockProviders::default();
        for op in OPS {
            assert!(
                dispatch(op, &json!({ "position_ms": 0, "rating": 0, "status": "normal", "tag": "x", "value": "y" }), &m, None, None).is_some(),
                "advertised op {op} is not dispatched"
            );
        }
    }
}
