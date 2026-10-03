//! Property-based fuzzing of the V6 command path: every op the server
//! advertises, with arbitrary `data`, through a handshaked `V6Session`.
//!
//! The op list is read from the capabilities the handshake advertises, so an op
//! added to a domain's `OPS` is fuzzed without touching this file. The
//! requirement: never panic, and answer every request exactly once, as a JSON
//! object carrying its id. `NullProviders` makes write ops safe to send.

use mbrc_core::providers::NullProviders;
use mbrc_core::server::commands_v6::capabilities;
use mbrc_core::server::session_v6::V6Session;
use proptest::prelude::*;
use serde_json::{Value, json};

/// Bounded recursive `serde_json::Value` strategy (no floats - NaN/precision).
fn json_value() -> impl Strategy<Value = Value> {
    let leaf = prop_oneof![
        Just(Value::Null),
        any::<bool>().prop_map(Value::Bool),
        any::<i64>().prop_map(|n| Value::Number(n.into())),
        ".{0,32}".prop_map(Value::String),
    ];
    leaf.prop_recursive(4, 48, 6, |inner| {
        prop_oneof![
            prop::collection::vec(inner.clone(), 0..6).prop_map(Value::Array),
            prop::collection::hash_map("[a-z_]{0,16}", inner, 0..6)
                .prop_map(|m| Value::Object(m.into_iter().collect())),
        ]
    })
}

fn advertised_ops() -> Vec<String> {
    let caps = capabilities();
    let ops = caps["ops"].as_array().expect("capabilities list ops");
    ops.iter()
        .filter_map(|op| op.as_str().map(str::to_string))
        .collect()
}

/// Field names the op handlers read, so arbitrary objects often hit a real one.
fn data() -> impl Strategy<Value = Value> {
    let field = prop::sample::select(vec![
        "offset",
        "limit",
        "src",
        "hash",
        "url",
        "id",
        "index",
        "query",
        "sort",
        "order",
        "album",
        "artist",
        "genre",
        "volume",
        "position_ms",
        "order_from",
        "order_to",
        "version",
        "paths",
        "tag",
        "value",
        "enabled",
        "mode",
        "code",
    ]);
    prop_oneof![
        json_value(),
        prop::collection::vec((field, json_value()), 0..5).prop_map(|kv| Value::Object(
            kv.into_iter().map(|(k, v)| (k.to_string(), v)).collect()
        )),
    ]
}

fn handshaked() -> V6Session {
    let mut session = V6Session::default();
    let hello = json!({
        "id": 0, "kind": "request", "op": "handshake",
        "data": { "protocol_version": 6, "client_id": "fuzz", "client_type": "cli" },
    });
    let out = session.handle_frame(&hello.to_string(), &NullProviders, None, None, None, None);
    let reply: Value = serde_json::from_str(&out.replies[0]).expect("handshake reply");
    assert!(reply.get("error").is_none(), "handshake rejected: {reply}");
    session
}

proptest! {
    #[test]
    fn every_advertised_op_answers_arbitrary_data_once(
        op in prop::sample::select(advertised_ops()),
        id in 1u64..u64::from(u32::MAX),
        data in data(),
    ) {
        let mut session = handshaked();
        let line = json!({ "id": id, "kind": "request", "op": op, "data": data }).to_string();
        let out = session.handle_frame(&line, &NullProviders, None, None, None, None);
        let replies: Vec<Value> = out
            .replies
            .iter()
            .map(|r| serde_json::from_str(r).expect("reply is JSON"))
            .collect();
        let answers = replies.iter().filter(|r| r["id"] == id).count();
        prop_assert_eq!(answers, 1, "{} answered {} times: {:?}", op, answers, replies);
    }

    #[test]
    fn a_handshaked_session_survives_any_line(line in "(?s).{0,512}") {
        let mut session = handshaked();
        let out = session.handle_frame(&line, &NullProviders, None, None, None, None);
        for reply in &out.replies {
            prop_assert!(serde_json::from_str::<Value>(reply).is_ok(), "non-JSON reply {}", reply);
        }
    }
}
