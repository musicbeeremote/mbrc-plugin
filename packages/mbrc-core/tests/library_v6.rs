//! Integration test: the V6 library domain over a real socket - the capability
//! advertisement and the `Page` envelope shape. Typed-item happy paths (browse
//! slicing, canonical tracks, album cover_hash) are unit-tested in
//! `commands_v6::library` with mock providers + a temp cache; here we prove the
//! wire path.

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

const HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"lib-test","client_type":"cli"}}"#;

#[test]
fn library_wire_paths() {
    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ..Config::default()
        },
    ));
    let net = server::start(core).expect("server starts");

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

    // Handshake advertises the library ops.
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
    for op in [
        "library_genres",
        "library_artists",
        "library_albums",
        "library_tracks",
    ] {
        assert!(ops.contains(&op), "caps missing {op}: {hs}");
    }

    // library_genres returns a well-formed Page (empty with NullProviders).
    writer
        .write_all(
            b"{\"id\":1,\"kind\":\"request\",\"op\":\"library_genres\",\"data\":{\"offset\":0}}\n",
        )
        .unwrap();
    let page = read_frame(&mut reader);
    assert_eq!(page["id"], 1);
    assert_eq!(page["data"]["total"], 0);
    assert_eq!(page["data"]["offset"], 0);
    assert!(page["data"]["items"].as_array().unwrap().is_empty());

    // library_tracks likewise pages cleanly with no library.
    writer
        .write_all(b"{\"id\":2,\"kind\":\"request\",\"op\":\"library_tracks\",\"data\":{}}\n")
        .unwrap();
    let tracks = read_frame(&mut reader);
    assert!(tracks["data"]["items"].is_array());

    // A bad offset type is a typed error.
    writer
        .write_all(b"{\"id\":3,\"kind\":\"request\",\"op\":\"library_albums\",\"data\":{\"offset\":\"x\"}}\n")
        .unwrap();
    assert_eq!(read_frame(&mut reader)["error"]["code"], "invalid_field");

    // playlist_list is advertised and pages cleanly (playlist domain shares the harness).
    assert!(
        ops.contains(&"playlist_list"),
        "caps missing playlist_list: {hs}"
    );
    writer
        .write_all(b"{\"id\":4,\"kind\":\"request\",\"op\":\"playlist_list\",\"data\":{}}\n")
        .unwrap();
    let pl = read_frame(&mut reader);
    assert_eq!(pl["id"], 4);
    assert!(pl["data"]["items"].is_array());

    net.stop();
}
