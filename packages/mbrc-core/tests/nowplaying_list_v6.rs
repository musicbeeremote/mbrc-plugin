//! Integration test: the V6 now-playing LIST domain over a real socket - the list
//! read + a mutation + capability advertisement (ops + the list-changed event).

use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::sync::Arc;
use std::time::Duration;

use serde_json::Value;

use mbrc_core::config::Config;
use mbrc_core::providers::NullProviders;
use mbrc_core::server;
use mbrc_core::state::Core;

fn free_port() -> u16 {
    std::net::TcpListener::bind("127.0.0.1:0")
        .unwrap()
        .local_addr()
        .unwrap()
        .port()
}

fn read_frame(reader: &mut BufReader<TcpStream>) -> Value {
    let mut line = String::new();
    reader.read_line(&mut line).expect("read v6 frame");
    serde_json::from_str(line.trim()).expect("v6 frame is JSON")
}

fn send(w: &mut TcpStream, line: &str) {
    w.write_all(line.as_bytes()).unwrap();
    w.write_all(b"\n").unwrap();
}

const HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"npl-test","client_type":"cli"}}"#;

#[test]
fn now_playing_list_wire_paths() {
    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ..Config::default()
        },
    ));
    let net = server::start(core).expect("server starts");

    let mut w = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let rs = w.try_clone().unwrap();
    rs.set_read_timeout(Some(Duration::from_secs(3))).unwrap();
    let mut r = BufReader::new(rs);

    send(&mut w, HANDSHAKE);
    let hs = read_frame(&mut r);
    let ops: Vec<&str> = hs["data"]["capabilities"]["ops"]
        .as_array()
        .unwrap()
        .iter()
        .filter_map(|v| v.as_str())
        .collect();
    for op in [
        "now_playing_list",
        "now_playing_list_play",
        "now_playing_queue",
    ] {
        assert!(ops.contains(&op), "caps missing {op}: {hs}");
    }
    assert!(hs["data"]["capabilities"]["events"]
        .as_array()
        .unwrap()
        .iter()
        .any(|e| e == "now_playing_list_changed"));

    // now_playing_list: empty but well-formed Page.
    send(
        &mut w,
        r#"{"id":1,"kind":"request","op":"now_playing_list","data":{}}"#,
    );
    let list = read_frame(&mut r);
    assert_eq!(list["data"]["total"], 0);
    assert!(list["data"]["items"].as_array().unwrap().is_empty());

    // A mutation returns an empty ack.
    send(
        &mut w,
        r#"{"id":2,"kind":"request","op":"now_playing_list_play","data":{"index":0}}"#,
    );
    let played = read_frame(&mut r);
    assert_eq!(played["id"], 2);
    assert_eq!(played["kind"], "response");

    // Missing required field is a typed error.
    send(
        &mut w,
        r#"{"id":3,"kind":"request","op":"now_playing_queue","data":{}}"#,
    );
    assert_eq!(read_frame(&mut r)["error"]["code"], "missing_field");

    net.stop();
}
