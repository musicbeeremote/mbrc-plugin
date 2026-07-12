//! Property-based fuzzing of the end-to-end client-input path: arbitrary wire
//! lines and arbitrary `{context, data}` command frames driven through
//! `Session::handle_frame`. This is the real untrusted surface - bytes straight
//! off a client socket - so the requirement is: never panic, whatever arrives.
//!
//! Uses `NullProviders` (returns defaults, never touches MusicBee), so the test
//! exercises the parsing/dispatch/handler `data` extraction (pagination, string/
//! int args, path arrays, ...) rather than the provider I/O. cargo-fuzz would be
//! the coverage-guided tool but needs nightly; the toolchain is pinned to stable.

use mbrc_core::providers::NullProviders;
use mbrc_core::server::session::Session;
use proptest::prelude::*;
use serde_json::Value;

/// Bounded recursive `serde_json::Value` strategy (no floats - NaN/precision).
fn json_value() -> impl Strategy<Value = Value> {
    let leaf = prop_oneof![
        Just(Value::Null),
        any::<bool>().prop_map(Value::from),
        any::<i64>().prop_map(Value::from),
        ".*".prop_map(Value::from),
    ];
    leaf.prop_recursive(4, 48, 6, |inner| {
        prop_oneof![
            prop::collection::vec(inner.clone(), 0..6).prop_map(Value::from),
            prop::collection::vec((".*", inner), 0..6)
                .prop_map(|entries| Value::Object(entries.into_iter().collect())),
        ]
    })
}

proptest! {
    // Any raw wire line: exercises lenient parse + context routing + the
    // pre-handshake force-close path. Must never panic.
    #[test]
    fn handle_frame_never_panics_on_arbitrary_line(line in "(?s).*") {
        let providers = NullProviders;
        let mut session = Session::default();
        let _ = session.handle_frame(&line, &providers, None, None, None);
    }

    // A handshaked session dispatching an arbitrary `{context, data}` frame: this
    // drives the command handlers' `data` parsing across every registered
    // context. Must never panic on malformed args.
    #[test]
    fn dispatch_never_panics_on_arbitrary_command(
        ctx in "[a-z0-9_]{0,24}",
        data in json_value(),
    ) {
        let providers = NullProviders;
        // Pretend the handshake completed so commands dispatch to handlers
        // instead of tripping the pre-handshake close.
        let mut session = Session {
            protocol_version: Some(4),
            platform: Some("Android".to_string()),
            ..Default::default()
        };
        let line = serde_json::json!({ "context": ctx, "data": data }).to_string();
        let _ = session.handle_frame(&line, &providers, None, None, None);
    }

    // Real command contexts with arbitrary data - concentrates fuzzing on the
    // handlers that actually parse structured args (pagination, indices, queries)
    // rather than spending most cases on unknown contexts.
    #[test]
    fn known_commands_never_panic_on_arbitrary_data(
        ctx in prop::sample::select(KNOWN_CONTEXTS),
        data in json_value(),
    ) {
        let providers = NullProviders;
        let mut session = Session {
            protocol_version: Some(4),
            platform: Some("iOS".to_string()),
            ..Default::default()
        };
        let line = serde_json::json!({ "context": ctx, "data": data }).to_string();
        let _ = session.handle_frame(&line, &providers, None, None, None);
    }
}

/// A spread of real command contexts that parse structured `data`, so the fuzzer
/// spends its budget on the handlers with the most parsing surface.
const KNOWN_CONTEXTS: &[&str] = &[
    "browsetracks",
    "browseartists",
    "browsealbums",
    "browsegenres",
    "libraryartistalbums",
    "libraryalbumtracks",
    "librarygenreartists",
    "nowplayinglist",
    "nowplayinglistmove",
    "nowplayinglistremove",
    "nowplayinglistplay",
    "nowplayingqueue",
    "playervolume",
    "playerseek",
    "librarysearchartist",
    "nowplayingsearch",
    "playlistplay",
];
