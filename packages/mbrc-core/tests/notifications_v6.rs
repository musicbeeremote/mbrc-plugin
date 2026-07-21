//! Integration test: library-changing MusicBee notifications emit the V6
//! `library_changed` event **imperatively** - from the dispatch path
//! (`state::dispatch_notification`), NOT the pure `notifications_v6::build`
//! builder (which returns nothing for them, since it has no owned `Arc<Core>`).
//! Verifies the marker actually reaches a subscribed V6 main over a real socket.

use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::sync::Arc;
use std::time::Duration;

use serde_json::{json, Value};

use mbrc_core::config::Config;
use mbrc_core::ffi::types::NotificationType;
use mbrc_core::providers::NullProviders;
use mbrc_core::server;
use mbrc_core::state::{self, Core};

fn free_port() -> u16 {
    std::net::TcpListener::bind("127.0.0.1:0")
        .unwrap()
        .local_addr()
        .unwrap()
        .port()
}

fn core(port: u16) -> Arc<Core> {
    Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ..Config::default()
        },
    ))
}

fn connect(port: u16) -> (TcpStream, BufReader<TcpStream>) {
    let writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let rs = writer.try_clone().unwrap();
    rs.set_read_timeout(Some(Duration::from_secs(3))).unwrap();
    (writer, BufReader::new(rs))
}

fn read_frame(reader: &mut BufReader<TcpStream>) -> Value {
    let mut line = String::new();
    reader.read_line(&mut line).expect("read v6 frame");
    serde_json::from_str(line.trim()).expect("v6 frame is JSON")
}

fn send(writer: &mut TcpStream, line: &str) {
    writer.write_all(line.as_bytes()).unwrap();
    writer.write_all(b"\n").unwrap();
}

fn handshake() -> String {
    json!({
        "id": 0, "kind": "request", "op": "handshake",
        "data": { "protocol_version": 6, "client_id": "notif-test", "client_type": "cli", "no_broadcast": false }
    })
    .to_string()
}

/// A subscribed V6 main receives `library_changed` when a `FileAddedToLibrary`
/// notification is dispatched - proving the imperative emission path fires (the
/// pure builder is silent for this notification; see `notifications_v6` unit tests).
#[test]
fn file_added_emits_library_changed_to_v6_main() {
    let port = free_port();
    let core = core(port);
    let net = server::start(core.clone()).expect("server starts");
    let (mut w, mut r) = connect(port);

    send(&mut w, &handshake());
    assert_eq!(read_frame(&mut r)["data"]["server_version"], 6);
    // Ping barrier: once its response returns, the connection is registered with
    // the V6 broadcaster, so the event can't race ahead of the subscription.
    send(&mut w, r#"{"id":1,"kind":"request","op":"ping","data":{}}"#);
    assert_eq!(read_frame(&mut r)["id"], 1);

    // FileAddedToLibrary emits `library_changed` imperatively (and nudges the
    // scanner). The pure builder returns nothing for it.
    state::dispatch_notification(&core, NotificationType::FileAddedToLibrary);

    let ev = read_frame(&mut r);
    assert_eq!(ev["kind"], "event", "{ev}");
    assert_eq!(ev["event"], "library_changed", "{ev}");

    net.stop();
}
