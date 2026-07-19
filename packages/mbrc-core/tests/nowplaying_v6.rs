//! Integration test: the V6 now-playing STATE domain over a real socket - the
//! state/position/lyrics reads, a write (seek), the capability advertisement (ops
//! + the new events), and event delivery to a subscribed V6 main.

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

const HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"np-test","client_type":"cli"}}"#;

#[test]
fn now_playing_state_reads_writes_and_events() {
    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ..Config::default()
        },
    ));
    let net = server::start(core.clone()).expect("server starts");

    let mut w = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let rs = w.try_clone().unwrap();
    rs.set_read_timeout(Some(Duration::from_secs(3))).unwrap();
    let mut r = BufReader::new(rs);

    // Handshake advertises the now-playing ops + the new events.
    send(&mut w, HANDSHAKE);
    let hs = read_frame(&mut r);
    let ops: Vec<&str> = hs["data"]["capabilities"]["ops"]
        .as_array()
        .unwrap()
        .iter()
        .filter_map(|v| v.as_str())
        .collect();
    for op in [
        "now_playing_state",
        "now_playing_lyrics",
        "now_playing_seek",
    ] {
        assert!(ops.contains(&op), "caps missing {op}: {hs}");
    }
    let events: Vec<&str> = hs["data"]["capabilities"]["events"]
        .as_array()
        .unwrap()
        .iter()
        .filter_map(|v| v.as_str())
        .collect();
    for ev in [
        "now_playing_changed",
        "cover_cache_changed",
        "library_changed",
    ] {
        assert!(events.contains(&ev), "caps missing event {ev}: {hs}");
    }

    // now_playing_state: nothing playing -> null track, a well-formed envelope.
    send(
        &mut w,
        r#"{"id":1,"kind":"request","op":"now_playing_state","data":{}}"#,
    );
    let st = read_frame(&mut r);
    assert!(st["data"]["track"].is_null());
    assert_eq!(st["data"]["position_ms"], 0);
    assert_eq!(st["data"]["lfm_status"], "normal");

    // now_playing_lyrics -> structured, "none" with no lyrics.
    send(
        &mut w,
        r#"{"id":2,"kind":"request","op":"now_playing_lyrics","data":{}}"#,
    );
    assert_eq!(read_frame(&mut r)["data"]["type"], "none");

    // now_playing_seek returns the (read-back) position.
    send(
        &mut w,
        r#"{"id":3,"kind":"request","op":"now_playing_seek","data":{"position_ms":1000}}"#,
    );
    let seek = read_frame(&mut r);
    assert_eq!(seek["id"], 3);
    assert!(seek["data"].get("position_ms").is_some());

    // A bad rating is a typed error.
    send(
        &mut w,
        r#"{"id":4,"kind":"request","op":"now_playing_set_rating","data":{"rating":99}}"#,
    );
    assert_eq!(read_frame(&mut r)["error"]["code"], "invalid_field");

    // Ping barrier ensures registration completed, then a now-playing event is
    // delivered to this subscribed main.
    send(&mut w, r#"{"id":5,"kind":"request","op":"ping","data":{}}"#);
    assert_eq!(read_frame(&mut r)["id"], 5);
    core.v6_broadcaster.broadcast(&[
        r#"{"kind":"event","event":"now_playing_changed","data":{"title":"X"}}"#.to_string(),
    ]);
    let ev = read_frame(&mut r);
    assert_eq!(ev["kind"], "event");
    assert_eq!(ev["event"], "now_playing_changed");

    net.stop();
}
