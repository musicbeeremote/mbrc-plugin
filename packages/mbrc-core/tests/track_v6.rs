//! Integration test: the V6 track domain over a real socket - the `cover_get`
//! wire path (etag short-circuit + miss), `track_get` not-found, and the
//! capability advertisement. The typed `track_get` happy path (tag parsing +
//! cover-hash resolution) is unit-tested in `commands_v6::track` against a mock
//! provider + a seeded cover store; here we prove the envelope/threading.

#![allow(clippy::unwrap_used)]

mod harness;

use std::time::Duration;

use harness::{advertised, connect, round_trip, v6_handshake};

#[test]
fn track_domain_wire_paths() {
    let (port, net) = harness::start();
    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));
    let mut ask = |request: &str| round_trip(&mut writer, &mut reader, request);

    // Handshake; the capabilities advertise the new track ops.
    let hs = ask(&v6_handshake("track-test", false));
    let ops = advertised(&hs, "ops");
    assert!(ops.contains(&"track_get"), "caps: {hs}");
    assert!(ops.contains(&"cover_get"), "caps: {hs}");

    // cover_get with client_hash == hash short-circuits to not_modified (no store
    // read needed).
    let nm = ask(
        r#"{"id":1,"kind":"request","op":"cover_get","data":{"hash":"abc","client_hash":"abc"}}"#,
    );
    assert_eq!(nm["id"], 1);
    assert_eq!(nm["data"]["not_modified"], true);

    // cover_get for an unknown hash -> not_found.
    let miss = ask(r#"{"id":2,"kind":"request","op":"cover_get","data":{"hash":"missing"}}"#);
    assert_eq!(miss["error"]["code"], "not_found");

    // track_get for an unknown src (NullProviders returns no tags) -> not_found.
    let tg = ask(r#"{"id":3,"kind":"request","op":"track_get","data":{"src":"C:/nope.mp3"}}"#);
    assert_eq!(tg["id"], 3);
    assert_eq!(tg["error"]["code"], "not_found");

    // A missing required field is a typed error, not a crash.
    let missing = ask(r#"{"id":4,"kind":"request","op":"cover_get","data":{}}"#);
    assert_eq!(missing["error"]["code"], "missing_field");

    net.stop();
}
