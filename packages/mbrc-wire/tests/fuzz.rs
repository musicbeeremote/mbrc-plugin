//! Property-based fuzzing of the legacy wire parser and the iOS sanitizers.
//!
//! The wire parser eats untrusted bytes straight off a client socket, so the
//! hard requirements are: never panic on any input, and never corrupt an input
//! that was already valid. cargo-fuzz would be the coverage-guided tool, but it
//! needs nightly and the repo is pinned to stable, so these run as proptest
//! cases under `cargo test` (and in CI/coverage) instead.

use mbrc_wire::{
    frame_line, parse_context, parse_lenient, sanitize_ios_quotes, FrameAccumulator, TERMINATOR,
};
use proptest::prelude::*;
use serde_json::Value;

/// A bounded recursive `serde_json::Value` strategy. Leaves are null/bool/i64/
/// string (no floats - NaN and precision defeat round-trip equality); containers
/// nest a few levels. Serializing one of these yields *valid* JSON, which the
/// lenient path must treat identically to the strict path.
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
                .prop_map(|entries| { Value::Object(entries.into_iter().collect()) }),
        ]
    })
}

proptest! {
    // The parser and sanitizers must never panic, whatever text arrives. `(?s).*`
    // spans arbitrary Unicode including newlines.
    #[test]
    fn parser_never_panics_on_arbitrary_text(s in "(?s).*") {
        let _ = parse_lenient(&s);
        let _ = parse_context(&s);
        let _ = sanitize_ios_quotes(&s);
    }

    // Framing must never panic on arbitrary byte chunks, and every frame it
    // yields must be terminator-free (the terminator is stripped).
    #[test]
    fn accumulator_never_panics_and_strips_terminator(chunks in prop::collection::vec(any::<Vec<u8>>(), 0..8)) {
        let mut acc = FrameAccumulator::default();
        for chunk in &chunks {
            acc.push_bytes(chunk);
            while let Some(frame) = acc.next_frame() {
                prop_assert!(!frame.contains(TERMINATOR));
            }
        }
    }

    // Valid JSON is a fixed point of the lenient path: parsing it leniently
    // yields exactly the strict parse (the `\'`/bare-identifier repairs only
    // ever fire on input that was already broken).
    #[test]
    fn lenient_equals_strict_on_valid_json(v in json_value()) {
        let text = serde_json::to_string(&v).unwrap();
        prop_assert_eq!(parse_lenient(&text), Some(v));
    }

    // The iOS quote sanitizer is lossless on valid JSON: serde_json never emits
    // the `\'` escape it rewrites, so valid JSON round-trips unchanged.
    #[test]
    fn quote_sanitizer_is_identity_on_valid_json(v in json_value()) {
        let text = serde_json::to_string(&v).unwrap();
        prop_assert_eq!(sanitize_ios_quotes(&text), text);
    }

    // A frame built from a value, terminated and fed back through the
    // accumulator, comes back out with its context intact.
    #[test]
    fn framed_context_round_trips(ctx in "[a-z]+", data in json_value()) {
        let line = serde_json::json!({ "context": ctx, "data": data }).to_string();
        let mut acc = FrameAccumulator::default();
        acc.push_bytes(frame_line(&line).as_bytes());
        let frame = acc.next_frame().expect("one complete frame");
        let parsed = parse_context(&frame);
        prop_assert_eq!(parsed.as_deref(), Some(ctx.as_str()));
    }
}
