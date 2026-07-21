//! V6 golden snapshot: for a fixed script of `(op, data)` requests driven at the
//! real V6 command dispatch against a populated fixture provider, snapshot the
//! exact response wire frame and diff it against a committed golden.
//!
//! Unlike the V4 goldens (an external oracle - the core must match what shipped
//! clients/plugin put on the wire), V6 is clean-slate with no external client, so
//! this is a SELF-SNAPSHOT: it can't prove correctness against a third party, but
//! it catches unintended drift - rename/reorder a field, change an enum string or
//! error shape, or alter the capability list, and the committed frame stops
//! matching, forcing a deliberate re-bless.
//!
//! The frames are built exactly as the server builds them (`session_v6` maps
//! dispatch results through `v6::response_ok`/`response_error`; `None` -> the
//! `unknown_op` error; the handshake -> `{server_version, capabilities}`), so the
//! golden is byte-identical to the wire.
//!
//! Covers are deliberately omitted (`cover_store: None`): `cache_cover`
//! re-encodes JPEG, so any `cover_hash`/image byte is platform-dependent and
//! unsafe to commit. Cover behaviour has its own deterministic unit tests in
//! `commands_v6::track`.
//!
//! Regenerate after an intended change:  `MBRC_BLESS=1 cargo test -p mbrc-core \
//!   --target i686-pc-windows-msvc --test v6_golden`

mod common;

use std::path::PathBuf;

use serde_json::{json, Value};

use mbrc_core::providers::Providers;
use mbrc_core::server::commands_v6;
use mbrc_wire::v6::{self, ErrorCode};

use common::FixtureProviders;

/// One scripted request and its human-readable name.
struct Case {
    name: &'static str,
    op: &'static str,
    data: Value,
}

fn case(name: &'static str, op: &'static str, data: Value) -> Case {
    Case { name, op, data }
}

/// A committed golden record: the op name plus the literal request/response wire
/// lines. Serialized one-per-line as JSON.
fn record(name: &str, request: &str, response: &str) -> String {
    serde_json::to_string(&json!({
        "name": name,
        "request": request,
        "response": response,
    }))
    .unwrap()
}

/// The domains and their scripts. Each becomes one `golden/v6/<file>` file.
fn suites() -> Vec<(&'static str, Vec<Case>)> {
    let page = json!({ "offset": 0, "limit": 100 });
    vec![
        (
            "system",
            vec![case("system_info", "system_info", json!({}))],
        ),
        (
            "player",
            vec![
                case("player_status", "player_status", json!({})),
                case("player_output", "player_output", json!({})),
                // Setters echo the new canonical value (read back from the
                // fixture state); locks their reply shape.
                case(
                    "player_set_volume",
                    "player_set_volume",
                    json!({ "volume": 50 }),
                ),
                case(
                    "player_set_mute",
                    "player_set_mute",
                    json!({ "muted": true }),
                ),
                case(
                    "player_set_shuffle",
                    "player_set_shuffle",
                    json!({ "mode": "shuffle" }),
                ),
                case(
                    "player_set_repeat",
                    "player_set_repeat",
                    json!({ "mode": "all" }),
                ),
            ],
        ),
        (
            "track",
            vec![case(
                "track_get",
                "track_get",
                json!({ "src": "C:\\Music\\s.mp3" }),
            )],
        ),
        (
            "nowplaying",
            vec![
                case("now_playing_state", "now_playing_state", json!({})),
                case("now_playing_details", "now_playing_details", json!({})),
                case("now_playing_position", "now_playing_position", json!({})),
                case("now_playing_lyrics", "now_playing_lyrics", json!({})),
                // Seek is a write, but its reply reads the position back (locks the
                // {position_ms, duration_ms} shape - not {}).
                case(
                    "now_playing_seek",
                    "now_playing_seek",
                    json!({ "position_ms": 30000 }),
                ),
                case(
                    "now_playing_set_lfm",
                    "now_playing_set_lfm",
                    json!({ "status": "love" }),
                ),
            ],
        ),
        (
            "nowplaying_list",
            vec![
                // Default (full list order) and the up-next (shuffle) view - both
                // items carry order + position + play_position.
                case("now_playing_list", "now_playing_list", page.clone()),
                case(
                    "now_playing_list_up_next",
                    "now_playing_list",
                    json!({ "offset": 0, "limit": 100, "up_next": true }),
                ),
            ],
        ),
        (
            "library",
            vec![
                case("library_genres", "library_genres", page.clone()),
                case("library_artists", "library_artists", page.clone()),
                case("library_albums", "library_albums", page.clone()),
                case("library_tracks", "library_tracks", page.clone()),
                case(
                    "library_radio",
                    "library_radio",
                    json!({ "offset": 0, "limit": 50 }),
                ),
            ],
        ),
        (
            "playlist",
            vec![case("playlist_list", "playlist_list", page.clone())],
        ),
        (
            "errors",
            vec![
                case("unknown_op", "no_such_op", json!({})),
                // track_get with no `src` -> missing_field.
                case("missing_field", "track_get", json!({})),
                // library_tracks with a non-integer offset -> invalid_field.
                case(
                    "invalid_field",
                    "library_tracks",
                    json!({ "offset": "nope" }),
                ),
            ],
        ),
    ]
}

/// Build the exact response wire line for one request, mirroring `session_v6`.
fn response_frame(providers: &dyn Providers, id: u64, op: &str, data: &Value) -> String {
    match commands_v6::dispatch(op, data, providers, None, None, None) {
        Some(Ok(v)) => v6::response_ok(id, v),
        Some(Err(e)) => v6::response_error(id, e.code, &e.message),
        None => v6::response_error(id, ErrorCode::UnknownOp, &format!("unknown op: {op}")),
    }
}

/// The handshake exchange, exactly as the server answers it.
fn handshake_record() -> String {
    let request = v6::handshake_request("golden-client", v6::ClientType::Android, false);
    let response = v6::response_ok(
        0,
        json!({
            "server_version": v6::PROTOCOL_VERSION,
            "capabilities": commands_v6::capabilities(),
        }),
    );
    record("handshake", &request, &response)
}

fn golden_dir() -> PathBuf {
    PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("tests")
        .join("golden")
        .join("v6")
}

/// Generate every domain's golden content (filename -> file body).
fn generate() -> Vec<(String, String)> {
    let providers = FixtureProviders;
    let mut files = Vec::new();

    // Handshake stands alone (session-level, not an op).
    files.push(("handshake.jsonl".to_string(), handshake_record() + "\n"));

    for (domain, cases) in suites() {
        let body: String = cases
            .iter()
            .enumerate()
            .map(|(i, c)| {
                let id = (i + 1) as u64;
                let request = v6::request(id, c.op, c.data.clone());
                let response = response_frame(&providers, id, c.op, &c.data);
                record(c.name, &request, &response) + "\n"
            })
            .collect();
        files.push((format!("{domain}.jsonl"), body));
    }
    files
}

#[test]
fn v6_wire_matches_golden() {
    let bless = std::env::var_os("MBRC_BLESS").is_some();
    let dir = golden_dir();
    if bless {
        std::fs::create_dir_all(&dir).unwrap();
    }

    let mut mismatches: Vec<String> = Vec::new();
    for (file, body) in generate() {
        let path = dir.join(&file);
        if bless {
            std::fs::write(&path, &body).unwrap();
            continue;
        }
        match std::fs::read_to_string(&path) {
            // Normalize the container's line endings: the frames are single JSON
            // lines, so a CRLF checkout (Windows autocrlf) must not fail the diff.
            Ok(committed) if committed.replace("\r\n", "\n") == body => {}
            Ok(committed) => mismatches.push(format!(
                "--- {file} DRIFT ---\nexpected (committed):\n{committed}\nactual (generated):\n{body}"
            )),
            Err(_) => mismatches.push(format!(
                "--- {file} MISSING ---\nno committed golden; run with MBRC_BLESS=1 to create it.\ngenerated:\n{body}"
            )),
        }
    }

    assert!(
        mismatches.is_empty(),
        "V6 wire goldens drifted (re-bless with MBRC_BLESS=1 if intended):\n\n{}",
        mismatches.join("\n")
    );
}
