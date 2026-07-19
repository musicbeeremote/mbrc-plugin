//! End-to-end test of `mbrc conform`: run the compiled binary against a minimal
//! but conformant server and assert it reports all checks passing.
//!
//! The mock accepts BOTH protocols on the port (one connection each, detected by
//! first-frame shape): the V6 envelope for the conformance checks, and legacy V4
//! `{context,data}` for the browse value-parity differential - answering V4
//! `browsegenres` with the same data V6 `library_genres` returns, so the
//! differential runs (not just skips) and passes.

use std::io::{BufRead, BufReader, Write};
use std::net::{TcpListener, TcpStream};
use std::process::Command;
use std::thread;

use mbrc_wire::v6::{self, ErrorCode};
use serde_json::{json, Value};

/// One canned genre, returned identically on both protocols so the differential
/// matches.
fn genres_items() -> Value {
    json!([{ "genre": "Rock", "count": 5 }])
}

/// Accept connections in a loop, one handler thread each; detect V4 vs V6 by the
/// first frame's shape (a `context` key = legacy, `kind`/`op` = V6).
fn serve(listener: TcpListener) -> thread::JoinHandle<()> {
    thread::spawn(move || {
        for stream in listener.incoming() {
            let Ok(stream) = stream else { break };
            thread::spawn(move || handle_conn(stream));
        }
    })
}

fn handle_conn(stream: TcpStream) {
    let mut reader = BufReader::new(stream.try_clone().unwrap());
    let mut writer = stream;
    let mut proto: Option<bool> = None; // Some(true) = V4, Some(false) = V6
    loop {
        let mut line = String::new();
        match reader.read_line(&mut line) {
            Ok(0) | Err(_) => break,
            Ok(_) => {}
        }
        let line = line.trim();
        if line.is_empty() {
            continue;
        }
        let is_v4 = *proto.get_or_insert_with(|| {
            serde_json::from_str::<Value>(line)
                .ok()
                .and_then(|v| v.get("context").map(|_| ()))
                .is_some()
        });
        let framed = if is_v4 {
            match handle_v4(line) {
                Some(body) => mbrc_wire::frame_line(&body),
                None => continue,
            }
        } else {
            let Ok(req) = v6::parse_request(line) else {
                continue;
            };
            v6::frame_line(&handle_v6(&req))
        };
        if writer.write_all(framed.as_bytes()).is_err() {
            break;
        }
    }
}

fn handle_v6(req: &v6::IncomingRequest) -> String {
    match req.op.as_str() {
        "handshake" => v6::response_ok(
            0,
            json!({
                "server_version": 6,
                "capabilities": {
                    "ops": ["handshake", "ping", "player_status", "library_genres", "now_playing_state"],
                    "events": ["play_state_changed"],
                },
            }),
        ),
        "ping" => v6::response_ok(req.id, req.data.clone()),
        "player_status" => v6::response_ok(
            req.id,
            json!({ "play_state": "stopped", "volume": 50, "muted": false, "shuffle": "off", "repeat": "none", "scrobbling": false }),
        ),
        "library_genres" => {
            if req.data.get("offset").is_some_and(Value::is_string) {
                v6::response_error(req.id, ErrorCode::InvalidField, "offset must be an integer")
            } else {
                v6::response_ok(
                    req.id,
                    json!({ "total": 1, "offset": 0, "items": genres_items() }),
                )
            }
        }
        "now_playing_state" => v6::response_ok(
            req.id,
            json!({ "track": Value::Null, "position_ms": 0, "duration_ms": 0, "lfm_status": "normal" }),
        ),
        _ => v6::response_error(req.id, ErrorCode::UnknownOp, "unknown op"),
    }
}

/// Legacy V4: complete the handshake, then answer `browsegenres` with the same
/// data the V6 side returns for `library_genres` (V4 wraps items under `data`).
fn handle_v4(line: &str) -> Option<String> {
    let v: Value = serde_json::from_str(line).ok()?;
    let ctx = v.get("context").and_then(Value::as_str)?;
    let body = match ctx {
        "player" => json!({ "context": "player", "data": "MusicBee" }),
        "protocol" => json!({ "context": "protocol", "data": 4 }),
        "browsegenres" => json!({
            "context": "browsegenres",
            "data": { "total": 1, "offset": 0, "limit": 1_000_000, "data": genres_items() },
        }),
        _ => return None,
    };
    Some(body.to_string())
}

#[test]
fn conform_passes_against_a_conformant_server() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let _server = serve(listener);

    let out = Command::new(env!("CARGO_BIN_EXE_mbrc"))
        .args([
            "conform",
            "--host",
            "127.0.0.1",
            "--port",
            &port.to_string(),
            "--wait-ms",
            "800",
        ])
        .output()
        .expect("run mbrc conform");

    let stdout = String::from_utf8_lossy(&out.stdout);
    assert!(
        out.status.success(),
        "conform reported failures:\n{stdout}\n---stderr---\n{}",
        String::from_utf8_lossy(&out.stderr)
    );
    assert!(
        stdout.contains("0 failures"),
        "expected 0 failures:\n{stdout}"
    );
    // The handshake + capability checks must have run.
    assert!(stdout.contains("handshake"), "{stdout}");
    assert!(stdout.contains("capability honesty"), "{stdout}");
    // The browse value-parity differential must have actually RUN (V4 reachable),
    // not been skipped.
    assert!(
        stdout.contains("browse parity: library_genres"),
        "browse differential did not run:\n{stdout}"
    );
}
