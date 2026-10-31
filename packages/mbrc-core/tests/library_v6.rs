//! Integration test: the V6 library domain over a real socket - the capability
//! advertisement and the `Page` envelope shape. Typed-item happy paths (browse
//! slicing, canonical tracks, album cover_hash) are unit-tested in
//! `commands_v6::library` with mock providers + a temp cache; here we prove the
//! wire path.

#![allow(clippy::unwrap_used)]

mod harness;

use std::time::Duration;

use harness::{advertised, connect, round_trip};

const HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"lib-test","client_type":"cli"}}"#;

#[test]
fn library_wire_paths() {
    let (port, net) = harness::start();
    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));
    let mut ask = |request: &str| round_trip(&mut writer, &mut reader, request);

    // Handshake advertises the library ops.
    let hs = ask(HANDSHAKE);
    let ops = advertised(&hs, "ops");
    for op in [
        "library_genres",
        "library_artists",
        "library_albums",
        "library_tracks",
    ] {
        assert!(ops.contains(&op), "caps missing {op}: {hs}");
    }

    // library_genres returns a well-formed Page (empty with NullProviders).
    let page = ask(r#"{"id":1,"kind":"request","op":"library_genres","data":{"offset":0}}"#);
    assert_eq!(page["id"], 1);
    assert_eq!(page["data"]["total"], 0);
    assert_eq!(page["data"]["offset"], 0);
    assert!(page["data"]["items"].as_array().unwrap().is_empty());

    // library_tracks likewise pages cleanly with no library.
    let tracks = ask(r#"{"id":2,"kind":"request","op":"library_tracks","data":{}}"#);
    assert!(tracks["data"]["items"].is_array());

    // A bad offset type is a typed error.
    let bad = ask(r#"{"id":3,"kind":"request","op":"library_albums","data":{"offset":"x"}}"#);
    assert_eq!(bad["error"]["code"], "invalid_field");

    // playlist_list is advertised and pages cleanly (playlist domain shares the harness).
    assert!(
        ops.contains(&"playlist_list"),
        "caps missing playlist_list: {hs}"
    );
    let pl = ask(r#"{"id":4,"kind":"request","op":"playlist_list","data":{}}"#);
    assert_eq!(pl["id"], 4);
    assert!(pl["data"]["items"].is_array());

    net.stop();
}
