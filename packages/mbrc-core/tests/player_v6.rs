//! Integration test: the V6 player domain over a real socket - a command
//! round-trip, event delivery to a subscribed V6 main, and the `no_broadcast`
//! opt-out.

#![allow(clippy::unwrap_used)]

mod harness;

use std::io::{BufRead, BufReader};
use std::net::TcpStream;
use std::sync::Arc;
use std::time::Duration;

use serde_json::json;

use mbrc_core::config::Config;
use mbrc_core::server::NetHandle;
use mbrc_core::state::Core;

use harness::{connect, null_core, read_frame, send, start_core};

/// A server and a connection to it that gives up reading after `timeout`.
fn serve(timeout: Duration) -> (Arc<Core>, NetHandle, TcpStream, BufReader<TcpStream>) {
    let (port, core, net) = start_core(|port| null_core(Config::for_test(port)));
    let (w, r) = connect(port, timeout);
    (core, net, w, r)
}

fn handshake(no_broadcast: bool) -> String {
    json!({
        "id": 0, "kind": "request", "op": "handshake",
        "data": { "protocol_version": 6, "client_id": "player-test", "client_type": "cli", "no_broadcast": no_broadcast }
    })
    .to_string()
}

#[test]
fn player_command_round_trip() {
    let (_, net, mut w, mut r) = serve(Duration::from_secs(3));

    send(&mut w, &handshake(false));
    assert_eq!(read_frame(&mut r)["data"]["server_version"], 6);

    // A setter returns its (read-back) value.
    send(
        &mut w,
        r#"{"id":1,"kind":"request","op":"player_set_volume","data":{"volume":55}}"#,
    );
    let resp = read_frame(&mut r);
    assert_eq!(resp["id"], 1);
    assert_eq!(resp["kind"], "response");
    assert!(
        resp["data"].get("volume").is_some(),
        "volume echoed: {resp}"
    );

    // player_status is a typed object.
    send(
        &mut w,
        r#"{"id":2,"kind":"request","op":"player_status","data":{}}"#,
    );
    let status = read_frame(&mut r);
    assert_eq!(status["id"], 2);
    for key in [
        "play_state",
        "volume",
        "muted",
        "shuffle",
        "repeat",
        "scrobbling",
    ] {
        assert!(
            status["data"].get(key).is_some(),
            "status has {key}: {status}"
        );
    }

    // A bad field is a typed error, not a crash.
    send(
        &mut w,
        r#"{"id":3,"kind":"request","op":"player_set_volume","data":{"volume":999}}"#,
    );
    assert_eq!(read_frame(&mut r)["error"]["code"], "invalid_field");

    net.stop();
}

/// Read one frame past a handshake+ping barrier, then broadcast a V6 event and
/// confirm a subscribed main receives it.
#[test]
fn event_reaches_v6_main() {
    let (core, net, mut w, mut r) = serve(Duration::from_secs(3));

    send(&mut w, &handshake(false));
    assert_eq!(read_frame(&mut r)["data"]["server_version"], 6);
    // Ping barrier: once its response returns, the server has finished registering
    // this connection with the V6 broadcaster.
    send(&mut w, r#"{"id":1,"kind":"request","op":"ping","data":{}}"#);
    assert_eq!(read_frame(&mut r)["id"], 1);

    core.v6_broadcaster.broadcast(&[
        r#"{"kind":"event","event":"volume_changed","data":{"volume":50}}"#.to_string(),
    ]);

    let ev = read_frame(&mut r);
    assert_eq!(ev["kind"], "event");
    assert_eq!(ev["event"], "volume_changed");
    assert_eq!(ev["data"]["volume"], 50);

    net.stop();
}

#[test]
fn no_broadcast_v6_receives_no_event() {
    let (core, net, mut w, mut r) = serve(Duration::from_millis(600));

    // Handshake as an aux (no_broadcast) socket + ping barrier.
    send(&mut w, &handshake(true));
    assert_eq!(read_frame(&mut r)["data"]["server_version"], 6);
    send(&mut w, r#"{"id":1,"kind":"request","op":"ping","data":{}}"#);
    assert_eq!(read_frame(&mut r)["id"], 1);

    core.v6_broadcaster.broadcast(&[
        r#"{"kind":"event","event":"volume_changed","data":{"volume":50}}"#.to_string(),
    ]);

    // The aux socket must NOT be subscribed: the next read times out (no frame).
    let mut line = String::new();
    let n = r.read_line(&mut line).unwrap_or(0);
    assert_eq!(
        n, 0,
        "a no_broadcast V6 socket must receive no events; got: {line}"
    );

    net.stop();
}
