//! Integration test: start the real TCP server and drive the V6 spine over a
//! socket - the handshake + a `ping` round-trip, a rejected handshake, and V4/V6
//! coexistence on the same port (the #118 §10 acceptance proof).

use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::sync::Arc;
use std::time::Duration;

use serde_json::Value;

use mbrc_core::config::Config;
use mbrc_core::providers::NullProviders;
use mbrc_core::server;
use mbrc_core::state::Core;

/// Grab a currently-free port by binding an ephemeral socket and releasing it.
fn free_port() -> u16 {
    std::net::TcpListener::bind("127.0.0.1:0")
        .unwrap()
        .local_addr()
        .unwrap()
        .port()
}

fn start(port: u16) -> server::NetHandle {
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ..Config::default()
        },
    ));
    server::start(core).expect("server should bind and start")
}

/// V6 frames are newline-delimited; `read_line` returns one frame at a time.
fn read_frame(reader: &mut BufReader<TcpStream>) -> Value {
    let mut line = String::new();
    reader.read_line(&mut line).expect("read v6 frame");
    serde_json::from_str(line.trim()).expect("v6 frame is JSON")
}

const HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"install-42","client_type":"android"}}"#;

#[test]
fn handshake_advertises_capabilities_and_system_info_round_trips() {
    let port = free_port();
    let net = start(port);

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

    writer
        .write_all(format!("{HANDSHAKE}\n").as_bytes())
        .unwrap();
    let hs = read_frame(&mut reader);
    assert_eq!(hs["data"]["server_version"], 6);
    // The handshake advertises the op/event surface.
    let ops = hs["data"]["capabilities"]["ops"]
        .as_array()
        .expect("ops list");
    let ops: Vec<&str> = ops.iter().filter_map(|v| v.as_str()).collect();
    assert!(ops.contains(&"player_status"), "capabilities: {hs}");
    assert!(ops.contains(&"system_info"), "capabilities: {hs}");
    assert!(hs["data"]["capabilities"]["events"]
        .as_array()
        .unwrap()
        .iter()
        .any(|e| e == "volume_changed"));

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
    let port = free_port();
    let net = start(port);

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

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
    let port = free_port();
    let net = start(port);

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

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
    let port = free_port();
    let net = start(port);

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

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

#[test]
fn v4_and_v6_clients_coexist_on_the_same_port() {
    // The money shot for #118 §10: a legacy V4 client and a V6 client complete
    // their handshakes against the same listener, routed by first-frame shape.
    let port = free_port();
    let net = start(port);

    // --- V4 client (CRLF, {context,data}) ---
    let mut v4 = TcpStream::connect(("127.0.0.1", port)).expect("connect v4");
    let v4_read = v4.try_clone().unwrap();
    v4_read
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut v4_reader = BufReader::new(v4_read);
    v4.write_all(
        concat!(
            r#"{"context":"player","data":"Android"}"#,
            "\r\n",
            r#"{"context":"protocol","data":{"protocol_version":4,"no_broadcast":false}}"#,
            "\r\n",
        )
        .as_bytes(),
    )
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
    let mut v6 = TcpStream::connect(("127.0.0.1", port)).expect("connect v6");
    let v6_read = v6.try_clone().unwrap();
    v6_read
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut v6_reader = BufReader::new(v6_read);
    v6.write_all(format!("{HANDSHAKE}\n").as_bytes()).unwrap();
    let hs = read_frame(&mut v6_reader);
    assert_eq!(hs["kind"], "response");
    assert_eq!(hs["data"]["server_version"], 6);

    net.stop();
}
