//! Integration test: the V6 now-playing LIST domain over a real socket - the list
//! read + a mutation + capability advertisement (ops + the list-changed event).

#![allow(clippy::unwrap_used)]

mod harness;

use std::time::Duration;

use harness::{advertised, connect, read_frame, send};

const HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"npl-test","client_type":"cli"}}"#;

#[test]
fn now_playing_list_wire_paths() {
    let (port, net) = harness::start();
    let (mut w, mut r) = connect(port, Duration::from_secs(3));

    send(&mut w, HANDSHAKE);
    let hs = read_frame(&mut r);
    let ops = advertised(&hs, "ops");
    for op in [
        "now_playing_list",
        "now_playing_list_play",
        "now_playing_queue",
    ] {
        assert!(ops.contains(&op), "caps missing {op}: {hs}");
    }
    assert!(advertised(&hs, "events").contains(&"now_playing_list_changed"));

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
