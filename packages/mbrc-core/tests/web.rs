//! Integration test: the real server answers HTTP on the command port.
//!
//! The unit tests cover the sniff and the route table in isolation; what only an
//! end-to-end test can prove is that an HTTP request and a JSON first frame both
//! reach their own transport over one listener.

#![allow(clippy::unwrap_used)]

mod harness;

use std::io::{BufRead, BufReader, Read, Write};
use std::net::TcpStream;
use std::sync::Arc;
use std::time::Duration;

use serde_json::Value;

use mbrc_core::config::Config;
use mbrc_core::server;
use mbrc_core::server::permissions::Role;
use mbrc_core::state::Core;

use harness::{
    closed_within_timeout, connect, null_core, read_frame, send, start_core, start_on_free_port,
    v4_handshake, v4_handshake_frames,
};

const V6_HANDSHAKE: &str = r#"{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"tcp-app","client_type":"android"}}"#;

fn with_pairing(port: u16) -> Config {
    Config {
        web_auth_required: true,
        ..Config::for_test(port)
    }
}

/// [`start_on_free_port`], with `prepare` run on the core before it serves.
fn start_prepared(
    config: impl Fn(u16) -> Config,
    prepare: impl Fn(&Arc<Core>),
) -> (u16, server::NetHandle) {
    let (port, _, net) = start_core(|port| {
        let core = null_core(config(port));
        prepare(&core);
        core
    });
    (port, net)
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
    let (port, net) = harness::start();

    let (status, body) = request(
        port,
        "GET /api/v6/capabilities HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\n\r\n",
    );
    net.stop();

    assert!(status.starts_with("HTTP/1.1 200"), "status was {status}");
    let value: Value = serde_json::from_str(&body).expect("capabilities are JSON");
    assert_eq!(
        value["permissions"]["party_mode"], false,
        "the caller is told what it may do: {body}"
    );
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
    let (port, net) = harness::start();

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
    let (port, net) = harness::start();

    let (status, _) = request(
        port,
        "GET /api/v6/capabilities HTTP/1.1\r\nHost: evil.example\r\nConnection: close\r\n\r\n",
    );
    net.stop();

    assert!(status.starts_with("HTTP/1.1 421"), "status was {status}");
}

#[test]
fn a_page_on_another_site_is_refused_even_at_a_valid_host() {
    let (port, net) = harness::start();

    let post = |origin: &str| {
        request(
            port,
            &format!(
                "POST /api/v6/player_status HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\n{origin}\
                 Content-Length: 2\r\nContent-Type: application/json\r\n\
                 Connection: close\r\n\r\n{{}}"
            ),
        )
        .0
    };
    let cross_site = post("Origin: https://evil.example\r\n");
    let opaque = post("Origin: null\r\n");
    let same_site = post(&format!("Origin: http://127.0.0.1:{port}\r\n"));
    let native = post("");
    net.stop();

    assert!(cross_site.starts_with("HTTP/1.1 403"), "got {cross_site}");
    assert!(opaque.starts_with("HTTP/1.1 403"), "got {opaque}");
    assert!(same_site.starts_with("HTTP/1.1 200"), "got {same_site}");
    assert!(native.starts_with("HTTP/1.1 200"), "got {native}");
}

#[test]
fn a_cross_site_websocket_upgrade_is_refused() {
    let (port, net) = harness::start();

    let (status, _) = request(
        port,
        &format!(
            "GET /ws HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nOrigin: https://evil.example\r\n\
             Connection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Version: 13\r\n\
             Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==\r\n\r\n"
        ),
    );
    net.stop();

    assert!(status.starts_with("HTTP/1.1 403"), "status was {status}");
}

/// A body with no JSON `Content-Type` is what a cross-site page can send with no
/// preflight, so it must never reach an op, even one that takes no arguments.
#[test]
fn an_op_without_a_json_content_type_is_refused_in_the_v6_shape() {
    let (port, net) = harness::start();

    let post = |headers: &str| {
        request(
            port,
            &format!(
                "POST /api/v6/player_status HTTP/1.1\r\nHost: 127.0.0.1\r\n{headers}\
                 Connection: close\r\n\r\n"
            ),
        )
    };
    let (no_type, no_type_body) = post("Content-Length: 0\r\n");
    let (plain, _) = post("Content-Length: 0\r\nContent-Type: text/plain\r\n");
    let (empty_json, _) = post("Content-Length: 0\r\nContent-Type: application/json\r\n");
    net.stop();

    assert!(no_type.starts_with("HTTP/1.1 415"), "got {no_type}");
    let value: Value = serde_json::from_str(&no_type_body).expect("error body is JSON");
    assert_eq!(value["error"]["code"], "malformed_frame");
    assert!(plain.starts_with("HTTP/1.1 415"), "got {plain}");
    assert!(
        empty_json.starts_with("HTTP/1.1 200"),
        "an empty JSON body means `{{}}`, got {empty_json}"
    );
}

#[test]
fn a_body_that_is_not_json_is_a_v6_error() {
    let (port, net) = harness::start();

    let (status, body) = request(
        port,
        "POST /api/v6/player_status HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 3\r\n\
         Content-Type: application/json\r\nConnection: close\r\n\r\n{{{",
    );
    net.stop();

    assert!(status.starts_with("HTTP/1.1 400"), "status was {status}");
    let value: Value = serde_json::from_str(&body).expect("error body is JSON");
    assert_eq!(value["error"]["code"], "malformed_frame");
}

#[test]
fn an_unknown_op_is_a_404_carrying_the_v6_error_code() {
    let (port, net) = harness::start();

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
fn party_mode_refuses_an_unpaired_caller_with_a_403_forbidden() {
    let (port, net) = start_prepared(Config::for_test, |core| core.party_mode.set_enabled(true));

    let (status, body) = request(
        port,
        "POST /api/v6/player_next HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 2\r\n\
         Content-Type: application/json\r\nConnection: close\r\n\r\n{}",
    );
    net.stop();

    assert!(status.starts_with("HTTP/1.1 403"), "status was {status}");
    let value: Value = serde_json::from_str(&body).expect("error body is JSON");
    assert_eq!(value["error"]["code"], "forbidden");
}

#[test]
fn party_mode_lets_a_paired_browser_act_on_the_role_the_host_gave_it() {
    let core_slot = std::sync::OnceLock::new();
    let (port, net) = start_prepared(Config::for_test, |core| {
        core.party_mode.set_enabled(true);
        let _ = core_slot.set(Arc::clone(core));
    });
    let core = core_slot.get().expect("prepared");
    let code = core.pairing.new_code_for(Role::Dj);
    let pair_body = format!(r#"{{"code":"{code}","label":"phone"}}"#);
    let (pair_status, paired) = request(
        port,
        &format!(
            "POST /api/pair HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: {}\r\n\
             Content-Type: application/json\r\nConnection: close\r\n\r\n{pair_body}",
            pair_body.len()
        ),
    );
    let paired: Value =
        serde_json::from_str(&paired).unwrap_or_else(|e| panic!("{pair_status}: {e}"));
    assert_eq!(
        paired["role"], "dj",
        "the code's role is granted on pairing"
    );
    let token = paired["token"].as_str().expect("a token").to_owned();

    let next = |auth: &str| {
        request(
            port,
            &format!(
                "POST /api/v6/player_next HTTP/1.1\r\nHost: 127.0.0.1\r\n{auth}Content-Length: 2\r\n\
                 Content-Type: application/json\r\nConnection: close\r\n\r\n{{}}"
            ),
        )
        .0
    };
    let as_dj = next(&format!("Authorization: Bearer {token}\r\n"));
    let unpaired = next("");
    net.stop();

    assert!(as_dj.starts_with("HTTP/1.1 200"), "paired dj got {as_dj}");
    assert!(
        unpaired.starts_with("HTTP/1.1 403"),
        "unpaired got {unpaired}"
    );
}

#[test]
fn the_handshake_op_is_not_offered_over_http() {
    let (port, net) = harness::start();

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
    let (port, net) = start_on_free_port(|port| Config {
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
fn with_pairing_on_a_socket_pairs_and_its_token_works_on_every_transport() {
    let core_slot = std::sync::OnceLock::new();
    let (port, net) = start_prepared(with_pairing, |core| {
        let _ = core_slot.set(Arc::clone(core));
    });
    let core = core_slot.get().expect("prepared");
    let timeout = Duration::from_secs(3);

    let (mut writer, mut reader) = connect(port, timeout);
    send(&mut writer, V6_HANDSHAKE);
    let handshake = read_frame(&mut reader);
    assert_eq!(handshake["data"]["pairing_required"], true);
    assert_eq!(handshake["data"]["paired"], false);

    send(
        &mut writer,
        r#"{"id":1,"kind":"request","op":"player_status","data":{}}"#,
    );
    assert_eq!(read_frame(&mut reader)["error"]["code"], "pairing_required");

    let code = core.pairing.new_code();
    send(
        &mut writer,
        &format!(r#"{{"id":2,"kind":"request","op":"pair","data":{{"code":"{code}"}}}}"#),
    );
    let paired = read_frame(&mut reader);
    let token = paired["data"]["token"]
        .as_str()
        .expect("a token")
        .to_owned();

    send(
        &mut writer,
        r#"{"id":3,"kind":"request","op":"player_status","data":{}}"#,
    );
    assert!(read_frame(&mut reader).get("error").is_none());

    let (mut writer, mut reader) = connect(port, timeout);
    send(
        &mut writer,
        &format!(
            r#"{{"id":0,"kind":"request","op":"handshake","data":{{"protocol_version":6,"client_id":"tcp-app-2","client_type":"android","token":"{token}"}}}}"#
        ),
    );
    assert_eq!(read_frame(&mut reader)["data"]["paired"], true);

    let (status, _) = request(
        port,
        &format!(
            "GET /api/v6/capabilities HTTP/1.1\r\nHost: 127.0.0.1\r\n\
             Authorization: Bearer {token}\r\nConnection: close\r\n\r\n"
        ),
    );
    net.stop();
    assert!(status.starts_with("HTTP/1.1 200"), "status was {status}");
}

#[test]
fn with_pairing_off_a_socket_needs_no_pairing() {
    let (port, net) = harness::start();
    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));
    send(&mut writer, V6_HANDSHAKE);
    assert_eq!(read_frame(&mut reader)["data"]["pairing_required"], false);
    send(
        &mut writer,
        r#"{"id":1,"kind":"request","op":"player_status","data":{}}"#,
    );
    let status = read_frame(&mut reader);
    net.stop();
    assert!(status.get("error").is_none(), "{status}");
}

/// Shipped V4/V5 apps have no way to pair, so pairing on must not cut them off.
#[test]
fn with_pairing_on_a_legacy_client_connects_unless_told_otherwise() {
    let (port, net) = start_on_free_port(with_pairing);
    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));
    v4_handshake(
        &mut writer,
        &mut reader,
        &v4_handshake_frames("Android", false),
    );
    net.stop();
}

#[test]
fn a_legacy_client_is_refused_when_the_option_is_off() {
    let (port, core, net) = start_core(|port| {
        null_core(Config {
            allow_unpaired_legacy_clients: false,
            ..with_pairing(port)
        })
    });
    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));
    writer
        .write_all(v4_handshake_frames("Android", false).as_bytes())
        .unwrap();
    assert!(closed_within_timeout(&mut reader));
    drop(core);
    net.stop();
}

#[test]
fn the_option_changes_nothing_while_pairing_is_off() {
    let (port, net) = start_on_free_port(|port| Config {
        allow_unpaired_legacy_clients: false,
        ..Config::for_test(port)
    });
    let (mut writer, mut reader) = connect(port, Duration::from_secs(3));
    v4_handshake(
        &mut writer,
        &mut reader,
        &v4_handshake_frames("Android", false),
    );
    net.stop();
}

#[test]
fn a_token_in_the_url_is_not_accepted() {
    let core_slot = std::sync::OnceLock::new();
    let (port, net) = start_prepared(with_pairing, |core| {
        let _ = core_slot.set(Arc::clone(core));
    });
    let core = core_slot.get().expect("prepared");
    let code = core.pairing.new_code();
    let local = "127.0.0.1".parse().unwrap();
    let token = core.pairing.redeem(&code, "x", local).unwrap().token;

    let (status, body) = request(
        port,
        &format!(
            "GET /api/events?token={token} HTTP/1.1\r\nHost: 127.0.0.1\r\n\
             Connection: close\r\n\r\n"
        ),
    );
    net.stop();
    assert!(status.starts_with("HTTP/1.1 401"), "status was {status}");
    let value: Value = serde_json::from_str(&body).expect("error body is JSON");
    assert_eq!(value["error"]["code"], "pairing_required");
}

#[test]
fn pair_status_says_whether_party_mode_is_on_so_the_pairing_screen_can_point_the_right_way() {
    let pair_status = |party: bool| {
        let (port, net) = start_prepared(
            |port| Config {
                web_auth_required: true,
                ..Config::for_test(port)
            },
            |core| core.party_mode.set_enabled(party),
        );
        let (_, body) = request(
            port,
            "GET /api/pair/status HTTP/1.1
Host: 127.0.0.1
Connection: close

",
        );
        net.stop();
        serde_json::from_str::<Value>(&body).unwrap()["party_mode"].clone()
    };

    assert_eq!(pair_status(false), false);
    assert_eq!(pair_status(true), true);
}

#[test]
fn http_can_be_turned_off_without_disturbing_the_json_protocols() {
    let (port, net) = start_on_free_port(|port| Config {
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

    let (mut json, mut reader) = harness::connect(port, Duration::from_secs(3));
    let value = harness::round_trip(
        &mut json,
        &mut reader,
        &harness::v6_handshake("web-test", false),
    );
    net.stop();

    assert!(
        !response.starts_with("HTTP/1.1 200"),
        "web_enabled=false must not serve HTTP, got: {response}"
    );
    assert_eq!(value["kind"], "response");
}

/// A browser that cannot hold a socket still has to be told what changes, so
/// the stream is the broadcast half of the protocol over plain HTTP. It needs
/// no handshake: it is one-way, and opening it is the whole subscription.
#[test]
fn the_event_stream_opens_without_a_handshake() {
    let (port, handle) = harness::start();

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
