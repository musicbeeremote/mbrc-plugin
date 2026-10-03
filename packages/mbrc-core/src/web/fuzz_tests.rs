//! Property-based fuzzing of the web remote's HTTP surface, driven through the
//! real router in process: arbitrary methods, paths, queries, headers and bodies
//! against every route, with pairing enforced and not, Party Mode on and off.
//!
//! The requirement is the one a browser on the LAN cannot break: no panic, no
//! 500, the security headers on every response, and JSON wherever JSON is
//! promised. Each case gets a fresh `Core` so pairing strikes from one case
//! never decide another.

use std::net::SocketAddr;
use std::sync::Arc;

use axum::body::Body;
use axum::http::{Method, Request, StatusCode, header};
use proptest::prelude::*;
use serde_json::Value;
use tower::ServiceExt;

use super::router::{WebState, app};
use crate::config::Config;
use crate::providers::NullProviders;
use crate::state::Core;

/// Every op the server advertises, so a new op is fuzzed the day it ships.
fn advertised_ops() -> Vec<String> {
    let caps = crate::server::commands_v6::capabilities();
    let ops = caps["ops"].as_array().expect("capabilities list ops");
    ops.iter()
        .filter_map(|op| op.as_str().map(str::to_string))
        .collect()
}

/// Bounded JSON, the same shape the socket fuzzers use.
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
            prop::collection::hash_map("[a-z_]{0,12}", inner, 0..6)
                .prop_map(|m| Value::Object(m.into_iter().collect())),
        ]
    })
}

fn method() -> impl Strategy<Value = Method> {
    prop::sample::select(vec![
        Method::GET,
        Method::POST,
        Method::HEAD,
        Method::PUT,
        Method::DELETE,
        Method::OPTIONS,
        Method::PATCH,
    ])
}

fn segment() -> impl Strategy<Value = String> {
    "[A-Za-z0-9._~%-]{0,24}"
}

/// A path, with the method its route answers to.
fn path() -> impl Strategy<Value = (String, Method)> {
    let get = |p: &str| (p.to_string(), Method::GET);
    prop_oneof![
        prop::sample::select(advertised_ops())
            .prop_map(|op| (format!("/api/v6/{op}"), Method::POST)),
        segment().prop_map(|op| (format!("/api/v6/{op}"), Method::POST)),
        Just(get("/api/v6/capabilities")),
        "[0-9a-f]{40}".prop_map(|h| (format!("/api/cover/{h}"), Method::GET)),
        segment().prop_map(|h| (format!("/api/cover/{h}"), Method::GET)),
        Just(get("/api/cover/now-playing")),
        Just(("/api/pair".to_string(), Method::POST)),
        Just(get("/api/pair/status")),
        Just(get("/api/events")),
        Just(get("/ws")),
        Just(get("/")),
        segment().prop_map(|s| (format!("/assets/{s}"), Method::GET)),
        prop::collection::vec(segment(), 0..5)
            .prop_map(|s| (format!("/{}", s.join("/")), Method::GET)),
    ]
}

fn query() -> impl Strategy<Value = String> {
    prop_oneof![
        Just(String::new()),
        "[A-Za-z0-9=&%._-]{0,48}".prop_map(|q| format!("?{q}")),
        "[a-z0-9]{0,40}".prop_map(|t| format!("?token={t}&v={t}")),
    ]
}

fn host() -> impl Strategy<Value = Option<String>> {
    prop_oneof![
        8 => Just(Some("127.0.0.1:3000".to_string())),
        1 => Just(None),
        2 => "[ -~]{0,40}".prop_map(Some),
    ]
}

fn token_header() -> impl Strategy<Value = Option<(header::HeaderName, String)>> {
    prop_oneof![
        Just(None),
        "[ -~]{0,48}".prop_map(|t| Some((header::COOKIE, format!("mbrc_token={t}")))),
        "[ -~]{0,48}".prop_map(|t| Some((header::AUTHORIZATION, format!("Bearer {t}")))),
        "[ -~]{0,48}".prop_map(|t| Some((header::AUTHORIZATION, t))),
    ]
}

fn body() -> impl Strategy<Value = (Option<&'static str>, Vec<u8>)> {
    prop_oneof![
        Just((None, Vec::new())),
        json_value().prop_map(|v| (Some("application/json"), v.to_string().into_bytes())),
        prop::collection::vec(any::<u8>(), 0..256).prop_map(|b| (Some("application/json"), b)),
        prop::collection::vec(any::<u8>(), 0..256).prop_map(|b| (Some("text/plain"), b)),
    ]
}

#[derive(Debug)]
struct Case {
    method: Method,
    uri: String,
    host: Option<String>,
    token: Option<(header::HeaderName, String)>,
    content_type: Option<&'static str>,
    body: Vec<u8>,
    auth_required: bool,
    party_mode: bool,
}

fn case() -> impl Strategy<Value = Case> {
    (
        prop_oneof![4 => Just(None), 1 => method().prop_map(Some)],
        path(),
        query(),
        host(),
        token_header(),
        body(),
        any::<bool>(),
        any::<bool>(),
    )
        .prop_map(
            |(other, (path, natural), query, host, token, (content_type, body), auth, party)| {
                Case {
                    method: other.unwrap_or(natural),
                    uri: format!("{path}{query}"),
                    host,
                    token,
                    content_type,
                    body,
                    auth_required: auth,
                    party_mode: party,
                }
            },
        )
}

/// What the router answered: status, content type and, unless it is an
/// endless event stream, the body.
struct Answer {
    status: StatusCode,
    content_type: String,
    has_security_headers: bool,
    body: Option<Vec<u8>>,
}

fn send(case: &Case) -> Option<Answer> {
    let mut builder = Request::builder()
        .method(case.method.clone())
        .uri(&case.uri);
    if let Some(host) = &case.host {
        builder = builder.header(header::HOST, host);
    }
    if let Some((name, value)) = &case.token {
        builder = builder.header(name, value);
    }
    if let Some(content_type) = case.content_type {
        builder = builder.header(header::CONTENT_TYPE, content_type);
    }
    // A URI or header the http crate refuses never reaches the server either.
    let request = builder.body(Body::from(case.body.clone())).ok()?;

    let config = Config {
        web_auth_required: case.auth_required,
        party_mode_enabled: case.party_mode,
        ..Config::for_test(0)
    };
    let state = WebState {
        core: Arc::new(Core::new(Arc::new(NullProviders), config)),
        peer: SocketAddr::from(([127, 0, 0, 1], 50_000)),
    };
    let runtime = tokio::runtime::Builder::new_current_thread()
        .enable_all()
        .build()
        .expect("runtime");
    Some(runtime.block_on(async move {
        let response = app(state)
            .oneshot(request)
            .await
            .expect("infallible router");
        let headers = response.headers();
        let content_type = headers
            .get(header::CONTENT_TYPE)
            .and_then(|v| v.to_str().ok())
            .unwrap_or_default()
            .to_string();
        let has_security_headers = headers.contains_key(header::CONTENT_SECURITY_POLICY)
            && headers.contains_key("x-content-type-options");
        let status = response.status();
        let body = if content_type.starts_with("text/event-stream") {
            None
        } else {
            let bytes = axum::body::to_bytes(response.into_body(), 64 * 1024 * 1024).await;
            Some(bytes.expect("body").to_vec())
        };
        Answer {
            status,
            content_type,
            has_security_headers,
            body,
        }
    }))
}

proptest! {
    #[test]
    fn any_request_gets_a_well_formed_answer(case in case()) {
        let Some(answer) = send(&case) else { return Ok(()) };
        prop_assert_ne!(answer.status, StatusCode::INTERNAL_SERVER_ERROR, "{:?}", case);
        prop_assert!(answer.has_security_headers, "missing security headers: {:?}", case);
        if answer.status.is_success() && answer.content_type.starts_with("application/json") {
            let body = answer.body.unwrap_or_default();
            prop_assert!(
                case.method == Method::HEAD || serde_json::from_slice::<Value>(&body).is_ok(),
                "2xx JSON body did not parse: {:?}", case
            );
        }
    }

    /// The opening bytes of every connection decide which protocol owns it.
    #[test]
    fn any_opening_bytes_are_routed_without_panicking(head in prop::collection::vec(any::<u8>(), 0..64)) {
        let _ = super::sniff(&head);
        let _ = crate::server::route::detect(&String::from_utf8_lossy(&head));
    }

    #[test]
    fn any_first_frame_is_routed_without_panicking(line in "(?s).{0,256}") {
        let _ = crate::server::route::detect(&line);
    }
}
