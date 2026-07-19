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
use mbrc_core::server;
use mbrc_core::state::Core;

/// Contexts the golden has as s2c but the core legitimately never sends:
/// `ping` is a server-initiated keepalive the core does not originate.
const EXCLUDED_S2C: &[&str] = &["ping"];

mod common;
use common::FixtureProviders;

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
        // This golden trace is the V4 wire; take the V4 frame set (`.0`).
        let (v4_frames, _v6_frames) = server::notifications::on_notification(&core, ntype);
        core.broadcaster.broadcast(&v4_frames);
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
