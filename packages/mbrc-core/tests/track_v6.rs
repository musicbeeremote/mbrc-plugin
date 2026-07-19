//! Integration test: the V6 track domain over a real socket - the `cover_get`
//! wire path (etag short-circuit + miss), `track_get` not-found, and the
//! capability advertisement. The typed `track_get` happy path (tag parsing +
//! cover-hash resolution) is unit-tested in `commands_v6::track` against a mock
//! provider + a seeded cover store; here we prove the envelope/threading.

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

fn read_frame(reader: &mut BufReader<TcpStream>) -> Value {
    let mut line = String::new();
    reader.read_line(&mut line).expect("read v6 frame");
    serde_json::from_str(line.trim()).expect("v6 frame is JSON")
}

const HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"track-test","client_type":"cli"}}"#;

#[test]
fn track_domain_wire_paths() {
    let port = free_port();
    let net = start(port);

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

    // Handshake; the capabilities advertise the new track ops.
    writer
        .write_all(format!("{HANDSHAKE}\n").as_bytes())
        .unwrap();
    let hs = read_frame(&mut reader);
    let ops: Vec<&str> = hs["data"]["capabilities"]["ops"]
        .as_array()
        .unwrap()
        .iter()
        .filter_map(|v| v.as_str())
        .collect();
    assert!(ops.contains(&"track_get"), "caps: {hs}");
    assert!(ops.contains(&"cover_get"), "caps: {hs}");

    // cover_get with client_hash == hash short-circuits to not_modified (no store
    // read needed).
    writer
        .write_all(
            b"{\"id\":1,\"kind\":\"request\",\"op\":\"cover_get\",\"data\":{\"hash\":\"abc\",\"client_hash\":\"abc\"}}\n",
        )
        .unwrap();
    let nm = read_frame(&mut reader);
    assert_eq!(nm["id"], 1);
    assert_eq!(nm["data"]["not_modified"], true);

    // cover_get for an unknown hash -> not_found.
    writer
        .write_all(b"{\"id\":2,\"kind\":\"request\",\"op\":\"cover_get\",\"data\":{\"hash\":\"missing\"}}\n")
        .unwrap();
    let miss = read_frame(&mut reader);
    assert_eq!(miss["error"]["code"], "not_found");

    // track_get for an unknown src (NullProviders returns no tags) -> not_found.
    writer
        .write_all(b"{\"id\":3,\"kind\":\"request\",\"op\":\"track_get\",\"data\":{\"src\":\"C:/nope.mp3\"}}\n")
        .unwrap();
    let tg = read_frame(&mut reader);
    assert_eq!(tg["id"], 3);
    assert_eq!(tg["error"]["code"], "not_found");

    // A missing required field is a typed error, not a crash.
    writer
        .write_all(b"{\"id\":4,\"kind\":\"request\",\"op\":\"cover_get\",\"data\":{}}\n")
        .unwrap();
    assert_eq!(read_frame(&mut reader)["error"]["code"], "missing_field");

    net.stop();
}
