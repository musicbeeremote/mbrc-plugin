//! Integration test: start the real TCP server and drive V6 over a socket - the
//! handshake plus a `ping` round-trip, a rejected handshake, the reply framing,
//! and V4/V6 coexistence on one port (the #118 §10 acceptance proof).

#![allow(clippy::unwrap_used)]

mod harness;

use std::io::{BufRead, Read, Write};
use std::time::Duration;

use serde_json::Value;

use harness::{connect, read_frame, send, v4_handshake_frames};

const HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"install-42","client_type":"android"}}"#;

#[test]
fn handshake_advertises_capabilities_and_system_info_round_trips() {
    let (port, net) = harness::start();

    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));

    send(&mut writer, HANDSHAKE);
    let hs = read_frame(&mut reader);
    assert_eq!(hs["data"]["server_version"], 6);
    // The handshake advertises the op/event surface.
    let ops = hs["data"]["capabilities"]["ops"]
        .as_array()
        .expect("ops list");
    let ops: Vec<&str> = ops.iter().filter_map(|v| v.as_str()).collect();
    assert!(ops.contains(&"player_status"), "capabilities: {hs}");
    assert!(ops.contains(&"system_info"), "capabilities: {hs}");
    assert!(
        hs["data"]["capabilities"]["events"]
            .as_array()
            .unwrap()
            .iter()
            .any(|e| e == "volume_changed")
    );

    // system_info returns the real plugin version + protocol version.
    writer
        .write_all(b"{\"id\":1,\"kind\":\"request\",\"op\":\"system_info\",\"data\":{}}\n")
        .unwrap();
    let info = read_frame(&mut reader);
    assert_eq!(info["id"], 1);
    assert_eq!(info["kind"], "response");
    assert_eq!(info["data"]["protocol_version"], 6);
    assert!(info["data"].get("plugin_version").is_some(), "info: {info}");

    net.stop();
}

#[test]
fn server_completes_v6_handshake_and_ping_round_trip() {
    let (port, net) = harness::start();

    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));

    // Handshake (id 0) + a ping (id 1), newline-framed, in one write.
    writer
        .write_all(
            format!(
                "{HANDSHAKE}\n{}\n",
                r#"{"id":1,"kind":"request","op":"ping","data":{"n":7}}"#
            )
            .as_bytes(),
        )
        .unwrap();

    let hs = read_frame(&mut reader);
    assert_eq!(hs["id"], 0);
    assert_eq!(hs["kind"], "response");
    assert_eq!(hs["data"]["server_version"], 6);

    let pong = read_frame(&mut reader);
    assert_eq!(pong["id"], 1);
    assert_eq!(pong["kind"], "response");
    assert_eq!(pong["data"]["n"], 7, "ping echoes its data back");

    net.stop();
}

#[test]
fn server_rejects_v6_handshake_missing_client_id() {
    let (port, net) = harness::start();

    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));

    writer
        .write_all(
            concat!(
                r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_type":"ios"}}"#,
                "\n",
            )
            .as_bytes(),
        )
        .unwrap();

    let err = read_frame(&mut reader);
    assert_eq!(err["kind"], "response");
    assert_eq!(err["error"]["code"], "missing_field");

    // The server closes the connection after a rejected handshake; next read = EOF.
    let mut rest = String::new();
    let n = reader.read_line(&mut rest).unwrap_or(0);
    assert_eq!(
        n, 0,
        "connection should be closed after handshake rejection"
    );

    net.stop();
}

#[test]
fn v6_op_before_handshake_is_unauthorized_and_closes() {
    let (port, net) = harness::start();

    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));

    // First frame is a V6 envelope (routes to V6) but not a handshake.
    writer
        .write_all(concat!(r#"{"id":5,"kind":"request","op":"ping","data":{}}"#, "\n").as_bytes())
        .unwrap();

    let err = read_frame(&mut reader);
    assert_eq!(err["id"], 5);
    assert_eq!(err["error"]["code"], "unauthorized");

    let mut rest = String::new();
    let n = reader.read_line(&mut rest).unwrap_or(0);
    assert_eq!(n, 0, "an op before the handshake closes the connection");

    net.stop();
}

/// The writer's terminator is fixed by routing, and a V6 client parses on `\n`
/// alone: a stray CR would land inside the next frame it reads.
#[test]
fn v6_replies_are_terminated_with_a_bare_lf() {
    let (port, net) = harness::start();

    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));
    send(&mut writer, HANDSHAKE);

    let mut buf = [0u8; 4096];
    let n = reader
        .get_mut()
        .read(&mut buf)
        .expect("read handshake reply");
    let raw = &buf[..n];
    assert_eq!(raw.last(), Some(&b'\n'), "V6 frame must end with LF");
    assert_ne!(
        raw.get(n.wrapping_sub(2)),
        Some(&b'\r'),
        "a V6 frame must not carry the legacy CR"
    );

    net.stop();
}

#[test]
fn v4_and_v6_clients_coexist_on_the_same_port() {
    // The money shot for #118 §10: a legacy V4 client and a V6 client complete
    // their handshakes against the same listener, routed by first-frame shape.
    let (port, net) = harness::start();

    // --- V4 client (CRLF, {context,data}) ---
    let (mut v4, mut v4_reader) = connect(port, Duration::from_secs(3));
    v4.write_all(v4_handshake_frames("Android", false).as_bytes())
        .unwrap();
    let mut player = String::new();
    v4_reader.read_line(&mut player).unwrap();
    let mut protocol = String::new();
    v4_reader.read_line(&mut protocol).unwrap();
    let player: Value = serde_json::from_str(player.trim()).unwrap();
    let protocol: Value = serde_json::from_str(protocol.trim()).unwrap();
    assert_eq!(player["context"], "player");
    assert_eq!(protocol, serde_json::json!({"context":"protocol","data":4}));

    // --- V6 client (LF, envelope) on the same port ---
    let (mut v6, mut v6_reader) = connect(port, Duration::from_secs(3));
    send(&mut v6, HANDSHAKE);
    let hs = read_frame(&mut v6_reader);
    assert_eq!(hs["kind"], "response");
    assert_eq!(hs["data"]["server_version"], 6);

    net.stop();
}
