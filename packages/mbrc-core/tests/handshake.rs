//! Integration test: start the real TCP server and drive a V4 handshake plus
//! keepalive/health frames over a socket, asserting the wire replies.

use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::sync::Arc;
use std::time::{Duration, Instant};

use serde_json::Value;

use mbrc_core::config::Config;
use mbrc_core::providers::NullProviders;
use mbrc_core::server;
use mbrc_core::state::Core;

/// Grab a currently-free port by binding an ephemeral socket and releasing it.
fn free_port() -> u16 {
    std::net::TcpListener::bind("127.0.0.1:0")
        .unwrap()
        .local_addr()
        .unwrap()
        .port()
}

#[test]
fn server_completes_v4_handshake_and_keepalive() {
    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ..Config::default()
        },
    ));
    let net = server::start(core).expect("server should bind and start");

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

    // Handshake + a ping + a health check, in one write.
    writer
        .write_all(
            concat!(
                r#"{"context":"player","data":"Android"}"#,
                "\r\n",
                r#"{"context":"protocol","data":{"protocol_version":4,"no_broadcast":false}}"#,
                "\r\n",
                r#"{"context":"ping","data":""}"#,
                "\r\n",
                r#"{"context":"verifyconnection","data":""}"#,
                "\r\n",
            )
            .as_bytes(),
        )
        .unwrap();

    // Expect four replies, in order.
    let mut replies = Vec::new();
    for _ in 0..4 {
        let mut line = String::new();
        reader.read_line(&mut line).expect("read reply");
        let v: Value = serde_json::from_str(line.trim()).expect("reply is JSON");
        replies.push((
            v["context"].as_str().unwrap().to_string(),
            v["data"].clone(),
        ));
    }

    net.stop();

    assert_eq!(replies[0], ("player".to_string(), Value::from("MusicBee")));
    assert_eq!(replies[1], ("protocol".to_string(), Value::from(4)));
    assert_eq!(replies[2], ("pong".to_string(), Value::from("")));
    assert_eq!(
        replies[3],
        ("verifyconnection".to_string(), Value::from(""))
    );
}

#[test]
fn server_rejects_pre_v4_client() {
    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ..Config::default()
        },
    ));
    let net = server::start(core).expect("server should bind and start");

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

    writer
        .write_all(concat!(r#"{"context":"protocol","data":3}"#, "\r\n").as_bytes())
        .unwrap();

    let mut line = String::new();
    reader.read_line(&mut line).expect("read reply");
    let v: Value = serde_json::from_str(line.trim()).expect("reply is JSON");
    assert_eq!(v["context"], Value::from("notallowed"));

    // The server closes the connection after rejecting; the next read hits EOF.
    let mut rest = String::new();
    let n = reader.read_line(&mut rest).unwrap_or(0);
    assert_eq!(n, 0, "connection should be closed after rejection");

    net.stop();
}

#[test]
fn reaps_idle_connection_that_never_handshakes() {
    // Model a socket that connects but never completes the handshake (negotiates
    // nothing). It is NOT a broadcast subscriber, so it gets no server pings; the
    // server must reap it once it stays silent past the un-handshaked window,
    // instead of leaking the socket forever.
    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ping_interval_secs: 1,
            unhandshaked_timeout_secs: 2,
            ..Config::default()
        },
    ));
    let net = server::start(core).expect("server should bind and start");

    let stream = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    // Read timeout comfortably longer than the un-handshaked window so EOF (the
    // reap) arrives before the timeout would.
    stream
        .set_read_timeout(Some(Duration::from_secs(6)))
        .unwrap();
    let mut reader = BufReader::new(stream);

    // Never handshake, never send anything. An un-handshaked socket is not a
    // broadcast subscriber, so it receives no pings; wait for the reap (EOF).
    let reaped;
    loop {
        let mut line = String::new();
        match reader.read_line(&mut line) {
            Ok(0) => {
                reaped = true; // EOF: the server closed the idle connection
                break;
            }
            Ok(_) => {} // no frames expected; ignore anything unexpected
            Err(e) => panic!(
                "connection was not reaped within the un-handshaked window (read error {e}); \
                 the server left an un-handshaked socket open"
            ),
        }
    }

    net.stop();

    assert!(
        reaped,
        "an idle un-handshaked connection should be reaped and closed"
    );
}

#[test]
fn broadcast_subscriber_is_not_reaped_while_silent() {
    // A receive-only broadcast channel (handshaked, broadcasts enabled) that
    // never sends inbound frames must stay alive: iOS subscribes for live state
    // and never sends anything back (not even a pong). The reaper must exempt
    // subscribers - reaping them is what made iOS churn hundreds of connections.
    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ping_interval_secs: 1,
            ..Config::default()
        },
    ));
    let net = server::start(core).expect("server should bind and start");

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_millis(1500)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

    // Handshake with broadcasts enabled, then go completely silent.
    writer
        .write_all(
            concat!(
                r#"{"context":"player","data":"iOS"}"#,
                "\r\n",
                r#"{"context":"protocol","data":{"protocol_version":4,"no_broadcast":false}}"#,
                "\r\n",
            )
            .as_bytes(),
        )
        .unwrap();
    for _ in 0..2 {
        let mut line = String::new();
        reader.read_line(&mut line).expect("handshake reply");
    }

    // Stay silent well past the idle timeout. We should keep receiving server
    // pings and never hit EOF (which would mean we were reaped).
    let start = Instant::now();
    let mut got_ping = false;
    let mut eof = false;
    while start.elapsed() < Duration::from_secs(4) {
        let mut line = String::new();
        match reader.read_line(&mut line) {
            Ok(0) => {
                eof = true;
                break;
            }
            Ok(_) => {
                if line.contains("\"ping\"") {
                    got_ping = true;
                }
            }
            Err(_) => {} // read timeout for this window; keep waiting
        }
    }

    net.stop();

    assert!(got_ping, "subscriber should receive server keepalive pings");
    assert!(!eof, "a silent broadcast subscriber must not be reaped");
}

#[test]
fn aux_socket_gets_no_ping_and_is_not_reaped_while_idle() {
    // An auxiliary request/response socket (no_broadcast=true) must NOT be pinged
    // (matching C#: only broadcast subscribers are pushed to), AND must not be
    // idle-reaped once handshaked - a real client (iOS especially) keeps its
    // command sockets open for reuse. Reaping them mid-idle is what left the app
    // non-responsive. Only un-handshaked sockets are reaped; dead ones are caught
    // by TCP keepalive.
    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ping_interval_secs: 1,
            unhandshaked_timeout_secs: 2,
            ..Config::default()
        },
    ));
    let net = server::start(core).expect("server should bind and start");

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_millis(500)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

    // Handshake as an aux socket (no_broadcast), then go silent.
    writer
        .write_all(
            concat!(
                r#"{"context":"player","data":"Android"}"#,
                "\r\n",
                r#"{"context":"protocol","data":{"protocol_version":4,"no_broadcast":true}}"#,
                "\r\n",
            )
            .as_bytes(),
        )
        .unwrap();
    for _ in 0..2 {
        let mut line = String::new();
        reader.read_line(&mut line).expect("handshake reply");
    }

    // Stay silent well past the un-handshaked window (2s): we must receive NO
    // pings and never hit EOF (the socket stays open for reuse).
    let start = Instant::now();
    let mut saw_ping = false;
    let mut eof = false;
    while start.elapsed() < Duration::from_secs(4) {
        let mut line = String::new();
        match reader.read_line(&mut line) {
            Ok(0) => {
                eof = true;
                break;
            }
            Ok(_) => {
                if line.contains("\"ping\"") {
                    saw_ping = true;
                }
            }
            Err(_) => {} // read timeout for this window; keep waiting
        }
    }

    net.stop();

    assert!(!saw_ping, "aux (no_broadcast) sockets must not be pinged");
    assert!(
        !eof,
        "a handshaked aux socket must not be idle-reaped (it is kept for reuse)"
    );
}

#[test]
fn new_main_supersedes_old_main_of_same_client() {
    // Two main (broadcast) connections carrying the same client_id: the newer one
    // supersedes the older, which the server closes.
    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ..Config::default()
        },
    ));
    let net = server::start(core).expect("server should bind and start");

    let handshake = concat!(
        r#"{"context":"player","data":"Android"}"#,
        "\r\n",
        r#"{"context":"protocol","data":{"protocol_version":4,"no_broadcast":false,"client_id":"dev-1"}}"#,
        "\r\n",
    );

    // Main A registers first.
    let mut a = TcpStream::connect(("127.0.0.1", port)).expect("connect A");
    let a_reader_stream = a.try_clone().unwrap();
    a_reader_stream
        .set_read_timeout(Some(Duration::from_secs(4)))
        .unwrap();
    let mut a_reader = BufReader::new(a_reader_stream);
    a.write_all(handshake.as_bytes()).unwrap();
    for _ in 0..2 {
        let mut line = String::new();
        a_reader.read_line(&mut line).expect("A handshake reply");
    }

    // Main B (same client_id) registers -> supersedes A.
    let mut b = TcpStream::connect(("127.0.0.1", port)).expect("connect B");
    b.write_all(handshake.as_bytes()).unwrap();

    // A should be closed by supersession: skip any queued frames until EOF.
    let mut closed = false;
    loop {
        let mut line = String::new();
        match a_reader.read_line(&mut line) {
            Ok(0) => {
                closed = true;
                break;
            }
            Ok(_) => continue,
            Err(_) => break, // read timeout: not closed
        }
    }

    net.stop();

    assert!(
        closed,
        "the superseded old main should be closed by the server"
    );
}

#[test]
fn broadcast_reaches_a_handshaked_client() {
    let port = free_port();
    let core = Arc::new(Core::new(
        Arc::new(NullProviders),
        Config {
            port,
            ..Config::default()
        },
    ));
    let net = server::start(core.clone()).expect("server should bind and start");

    let mut writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let reader_stream = writer.try_clone().unwrap();
    reader_stream
        .set_read_timeout(Some(Duration::from_secs(3)))
        .unwrap();
    let mut reader = BufReader::new(reader_stream);

    // Handshake with broadcasts enabled, and read both replies so we know the
    // server has processed the protocol frame (and thus registered us).
    writer
        .write_all(
            concat!(
                r#"{"context":"player","data":"Android"}"#,
                "\r\n",
                r#"{"context":"protocol","data":{"protocol_version":4,"no_broadcast":false}}"#,
                "\r\n",
            )
            .as_bytes(),
        )
        .unwrap();
    for _ in 0..2 {
        let mut line = String::new();
        reader.read_line(&mut line).expect("handshake reply");
    }

    // Fan out a broadcast; the registered client should receive it.
    core.broadcaster
        .broadcast(&[r#"{"context":"playermute","data":true}"#.to_string()]);

    let mut line = String::new();
    reader.read_line(&mut line).expect("broadcast frame");
    let v: Value = serde_json::from_str(line.trim()).unwrap();
    assert_eq!(
        v,
        serde_json::json!({"context": "playermute", "data": true})
    );

    net.stop();
}
