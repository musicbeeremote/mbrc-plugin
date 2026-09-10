//! Integration test: the real server answers HTTP on the command port.
//!
//! The unit tests cover the sniff and the route table in isolation; what only an
//! end-to-end test can prove is that an HTTP request and a JSON first frame both
//! reach their own transport over one listener.

#![allow(clippy::unwrap_used)]

use std::io::{BufRead, BufReader, Read, Write};
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

fn start(config: Config) -> server::NetHandle {
    let core = Arc::new(Core::new(Arc::new(NullProviders), config));
    server::start(core).expect("server should bind and start")
}

/// Sends one HTTP/1.1 request and returns `(status line, body)`.
fn request(port: u16, request: &str) -> (String, String) {
    let mut stream = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    stream
        .set_read_timeout(Some(Duration::from_secs(5)))
        .unwrap();
    stream.write_all(request.as_bytes()).unwrap();
    stream.flush().unwrap();

    let mut raw = String::new();
    // The server keeps the connection alive, so read to the declared length
    // rather than to EOF.
    let mut reader = BufReader::new(stream);
    let mut length = 0usize;
    loop {
        let mut line = String::new();
        if reader.read_line(&mut line).unwrap() == 0 {
            break;
        }
        raw.push_str(&line);
        if let Some(value) = line.to_ascii_lowercase().strip_prefix("content-length:") {
            length = value.trim().parse().unwrap_or(0);
        }
        if line == "\r\n" {
            break;
        }
    }
    let mut body = vec![0u8; length];
    reader.read_exact(&mut body).unwrap();

    let status = raw.lines().next().unwrap_or_default().to_string();
    (status, String::from_utf8_lossy(&body).into_owned())
}

#[test]
fn capabilities_are_served_over_http_on_the_command_port() {
    let port = free_port();
    let net = start(Config::for_test(port));

    let (status, body) = request(
        port,
        "GET /api/v6/capabilities HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n",
    );
    net.stop();

    assert!(status.starts_with("HTTP/1.1 200"), "status was {status}");
    let value: Value = serde_json::from_str(&body).expect("capabilities are JSON");
    assert!(
        value["ops"]
            .as_array()
            .expect("ops")
            .contains(&Value::String("player_play".into())),
        "the HTTP catalog is the same V6 catalog: {body}"
    );
}

/// `now-playing` is a route, not a hash. Registered after `/api/cover/{hash}` it
/// would be swallowed by it and rejected as a malformed content hash, which is a
/// 400 that looks nothing like the cause.
#[test]
fn the_now_playing_cover_route_is_not_read_as_a_hash() {
    let port = free_port();
    let net = start(Config::for_test(port));

    let (status, body) = request(
        port,
        "GET /api/cover/now-playing HTTP/1.1
Host: 127.0.0.1
Connection: close

",
    );
    net.stop();

    // NullProviders plays nothing, so the honest answer is 404 - but a 404 for
    // having nothing to show, never a 400 for the path not parsing as a hash.
    assert!(status.starts_with("HTTP/1.1 404"), "status was {status}");
    assert!(
        !body.contains("not a content hash"),
        "the hash route answered a route that is not a hash: {body}"
    );
}

#[test]
fn a_rebinding_host_is_refused() {
    let port = free_port();
    let net = start(Config::for_test(port));

    let (status, _) = request(
        port,
        "GET /api/v6/capabilities HTTP/1.1\r\nHost: evil.example\r\nConnection: close\r\n\r\n",
    );
    net.stop();

    assert!(status.starts_with("HTTP/1.1 421"), "status was {status}");
}

#[test]
fn an_unknown_op_is_a_404_carrying_the_v6_error_code() {
    let port = free_port();
    let net = start(Config::for_test(port));

    let (status, body) = request(
        port,
        "POST /api/v6/not_an_op HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 2\r\n\
         Content-Type: application/json\r\nConnection: close\r\n\r\n{}",
    );
    net.stop();

    assert!(status.starts_with("HTTP/1.1 404"), "status was {status}");
    let value: Value = serde_json::from_str(&body).expect("error body is JSON");
    assert_eq!(value["error"]["code"], "unknown_op");
}

#[test]
fn the_handshake_op_is_not_offered_over_http() {
    let port = free_port();
    let net = start(Config::for_test(port));

    let (status, _) = request(
        port,
        "POST /api/v6/handshake HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 2\r\n\
         Content-Type: application/json\r\nConnection: close\r\n\r\n{}",
    );
    net.stop();

    assert!(status.starts_with("HTTP/1.1 404"), "status was {status}");
}

#[test]
fn pairing_is_required_only_when_the_setting_says_so() {
    let port = free_port();
    let net = start(Config {
        web_auth_required: true,
        ..Config::for_test(port)
    });

    let (status, _) = request(
        port,
        "GET /api/v6/capabilities HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n",
    );
    let (open_status, open_body) = request(
        port,
        "GET /api/pair/status HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n",
    );
    net.stop();

    assert!(status.starts_with("HTTP/1.1 401"), "status was {status}");
    assert!(
        open_status.starts_with("HTTP/1.1 200"),
        "the pairing screen must stay reachable: {open_status}"
    );
    assert_eq!(
        serde_json::from_str::<Value>(&open_body).unwrap()["auth_required"],
        true
    );
}

#[test]
fn http_can_be_turned_off_without_disturbing_the_json_protocols() {
    let port = free_port();
    let net = start(Config {
        web_enabled: false,
        ..Config::for_test(port)
    });

    let mut stream = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    stream
        .write_all(b"GET /api/v6/capabilities HTTP/1.1\r\nHost: 127.0.0.1\r\n\r\n")
        .unwrap();
    let mut response = String::new();
    let _ = stream.read_to_string(&mut response);

    let mut json = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    json.set_read_timeout(Some(Duration::from_secs(3))).unwrap();
    json.write_all(
        b"{\"id\":0,\"kind\":\"request\",\"op\":\"handshake\",\
          \"data\":{\"protocol_version\":6,\"client_id\":\"web-test\",\"client_type\":\"cli\"}}\n",
    )
    .unwrap();
    let mut line = String::new();
    BufReader::new(json).read_line(&mut line).unwrap();
    net.stop();

    assert!(
        !response.starts_with("HTTP/1.1 200"),
        "web_enabled=false must not serve HTTP, got: {response}"
    );
    let value: Value = serde_json::from_str(&line).expect("V6 still answers");
    assert_eq!(value["kind"], "response");
}

/// A browser that cannot hold a socket still has to be told what changes, so
/// the stream is the broadcast half of the protocol over plain HTTP. It needs
/// no handshake: it is one-way, and opening it is the whole subscription.
#[test]
fn the_event_stream_opens_without_a_handshake() {
    let port = free_port();
    let handle = start(Config::for_test(port));

    let mut stream = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    stream
        .set_read_timeout(Some(Duration::from_secs(5)))
        .unwrap();
    stream
        .write_all(
            format!(
                "GET /api/events HTTP/1.1
Host: 127.0.0.1:{port}

"
            )
            .as_bytes(),
        )
        .unwrap();
    stream.flush().unwrap();

    // Headers only: the body stays open for as long as the client listens.
    let mut reader = BufReader::new(stream);
    let mut headers = String::new();
    loop {
        let mut line = String::new();
        if reader.read_line(&mut line).unwrap() == 0 {
            break;
        }
        headers.push_str(&line);
        if line
            == "
"
        {
            break;
        }
    }

    assert!(headers.starts_with("HTTP/1.1 200"), "{headers}");
    assert!(
        headers.to_ascii_lowercase().contains("text/event-stream"),
        "{headers}"
    );
    handle.stop();
}
