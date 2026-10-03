//! Transport behaviour on a real loopback server: frames that arrive a byte at
//! a time, split at arbitrary points or coalesced, from several connections at
//! once, and the Android pattern of one broadcast socket plus auxiliaries that
//! share its `client_id`.
//!
//! Chunkings come from a seeded generator, so a failure names its seed and
//! reproduces.

#![allow(clippy::unwrap_used)]

use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::sync::Arc;
use std::thread;
use std::time::{Duration, Instant};

use serde_json::Value;

use mbrc_core::config::Config;
use mbrc_core::providers::NullProviders;
use mbrc_core::server;
use mbrc_core::state::Core;

const V4_HANDSHAKE: &str = concat!(
    r#"{"context":"player","data":"Android"}"#,
    "\r\n",
    r#"{"context":"protocol","data":{"protocol_version":4,"no_broadcast":true}}"#,
    "\r\n",
);

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

/// xorshift64*, enough to vary chunk sizes reproducibly.
struct Rng(u64);

impl Rng {
    fn below(&mut self, n: usize) -> usize {
        self.0 ^= self.0 >> 12;
        self.0 ^= self.0 << 25;
        self.0 ^= self.0 >> 27;
        (self.0.wrapping_mul(0x2545_F491_4F6C_DD1D) % n as u64) as usize
    }
}

struct Client {
    writer: TcpStream,
    reader: BufReader<TcpStream>,
}

impl Client {
    fn connect(port: u16) -> Self {
        let writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
        writer.set_nodelay(true).unwrap();
        let read = writer.try_clone().unwrap();
        read.set_read_timeout(Some(Duration::from_secs(5))).unwrap();
        Self {
            writer,
            reader: BufReader::new(read),
        }
    }

    /// Writes `bytes` in chunks of at most `max_chunk`, pausing `gap` between.
    fn send_chunked(&mut self, bytes: &[u8], rng: &mut Rng, max_chunk: usize, gap: Duration) {
        let mut rest = bytes;
        while !rest.is_empty() {
            let n = 1 + rng.below(max_chunk.min(rest.len()));
            self.writer.write_all(&rest[..n]).unwrap();
            rest = &rest[n..];
            if !gap.is_zero() {
                thread::sleep(gap);
            }
        }
    }

    fn read_frame(&mut self) -> Value {
        let mut line = String::new();
        self.reader.read_line(&mut line).expect("read frame");
        serde_json::from_str(line.trim()).unwrap_or_else(|e| panic!("not JSON ({e}): {line:?}"))
    }

    /// Reads until EOF or the read timeout; true when the server closed.
    fn closed_within_timeout(&mut self) -> bool {
        loop {
            let mut line = String::new();
            match self.reader.read_line(&mut line) {
                Ok(0) => return true,
                Ok(_) => {}
                Err(_) => return false,
            }
        }
    }
}

fn v6_handshake(client_id: &str) -> String {
    format!(
        r#"{{"id":0,"kind":"request","op":"handshake","data":{{"protocol_version":6,"client_id":"{client_id}","client_type":"android"}}}}"#
    ) + "\n"
}

fn v6_ping(id: u64, data: &str) -> String {
    format!(r#"{{"id":{id},"kind":"request","op":"ping","data":"{data}"}}"#) + "\n"
}

/// A V6 script whose ping payloads straddle UTF-8 boundaries when split.
fn v6_script(client_id: &str, tag: &str) -> (String, Vec<(u64, String)>) {
    let pings: Vec<(u64, String)> = (1..=4)
        .map(|id| (id, format!("{tag}-{id} héllo ☃ 🎵")))
        .collect();
    let mut script = v6_handshake(client_id);
    for (id, data) in &pings {
        script += &v6_ping(*id, data);
    }
    (script, pings)
}

/// Reads the handshake reply, then checks every ping is echoed once, in order.
fn expect_v6_echoes(client: &mut Client, pings: &[(u64, String)], context: &str) {
    let hs = client.read_frame();
    assert_eq!(hs["id"], 0, "{context}: handshake reply first");
    assert_eq!(hs["data"]["server_version"], 6, "{context}: handshake ok");
    for (id, data) in pings {
        let reply = client.read_frame();
        assert_eq!(reply["id"], *id, "{context}: reply id");
        assert_eq!(reply["data"], data.as_str(), "{context}: echo of ping {id}");
    }
}

/// Reads the V4 handshake replies and the replies to `frames`, as contexts.
fn v4_reply_contexts(client: &mut Client, frames: usize) -> Vec<String> {
    (0..2 + frames)
        .map(|_| client.read_frame()["context"].as_str().unwrap().to_string())
        .collect()
}

const V4_REQUESTS: &str = concat!(
    r#"{"context":"playerstatus","data":""}"#,
    "\r\n",
    r#"{"context":"verifyconnection","data":""}"#,
    "\r\n",
);

#[test]
fn v4_frames_sent_a_byte_at_a_time_get_the_same_replies_as_one_write() {
    let port = free_port();
    let net = start(Config::for_test(port));
    let script = format!("{V4_HANDSHAKE}{V4_REQUESTS}");

    let mut whole = Client::connect(port);
    whole.writer.write_all(script.as_bytes()).unwrap();
    let expected = v4_reply_contexts(&mut whole, 2);

    let mut dribbled = Client::connect(port);
    let mut rng = Rng(1);
    dribbled.send_chunked(script.as_bytes(), &mut rng, 1, Duration::from_millis(1));
    let got = v4_reply_contexts(&mut dribbled, 2);

    net.stop();
    assert_eq!(got, expected);
}

#[test]
fn v6_frames_sent_a_byte_at_a_time_are_each_answered_once() {
    let port = free_port();
    let net = start(Config::for_test(port));
    let (script, pings) = v6_script("transport-dribble", "d");

    let mut client = Client::connect(port);
    client.send_chunked(script.as_bytes(), &mut Rng(2), 1, Duration::from_millis(1));
    expect_v6_echoes(&mut client, &pings, "byte at a time");

    net.stop();
}

#[test]
fn v6_frames_split_at_any_point_including_inside_a_utf8_character_round_trip() {
    let port = free_port();
    let net = start(Config::for_test(port));

    for seed in 1..=20u64 {
        let (script, pings) = v6_script(&format!("transport-split-{seed}"), "s");
        let mut client = Client::connect(port);
        // Seed 1 sends the whole script in one write; the rest split it.
        let max_chunk = if seed == 1 {
            script.len()
        } else {
            1 + seed as usize * 3
        };
        let gap = Duration::from_millis(1);
        client.send_chunked(script.as_bytes(), &mut Rng(seed), max_chunk, gap);
        expect_v6_echoes(&mut client, &pings, &format!("seed {seed}"));
    }

    net.stop();
}

#[test]
fn concurrent_v4_and_v6_connections_interleaving_partial_frames_get_their_own_replies() {
    let port = free_port();
    let net = start(Config::for_test(port));
    let script = format!("{V4_HANDSHAKE}{V4_REQUESTS}");
    let mut baseline = Client::connect(port);
    baseline.writer.write_all(script.as_bytes()).unwrap();
    let v4_expected = v4_reply_contexts(&mut baseline, 2);

    let workers: Vec<_> = (0..8u64)
        .map(|k| {
            let v4_script = script.clone();
            let v4_expected = v4_expected.clone();
            thread::spawn(move || {
                let mut client = Client::connect(port);
                let mut rng = Rng(100 + k);
                let gap = Duration::from_millis(1);
                if k % 2 == 0 {
                    client.send_chunked(v4_script.as_bytes(), &mut rng, 7, gap);
                    assert_eq!(v4_reply_contexts(&mut client, 2), v4_expected, "conn {k}");
                } else {
                    let (v6, pings) = v6_script(&format!("transport-conc-{k}"), &format!("c{k}"));
                    client.send_chunked(v6.as_bytes(), &mut rng, 7, gap);
                    expect_v6_echoes(&mut client, &pings, &format!("conn {k}"));
                }
            })
        })
        .collect();
    let failures = workers.into_iter().filter_map(|w| w.join().err()).count();

    net.stop();
    assert_eq!(failures, 0, "every connection gets exactly its own replies");
}

#[test]
fn android_auxiliary_sockets_share_a_client_id_without_superseding_the_main_one() {
    let port = free_port();
    let net = start(Config {
        ping_interval_secs: 1,
        ..Config::for_test(port)
    });
    let handshake = |no_broadcast: bool| {
        format!(
            "{}\r\n{}\r\n",
            r#"{"context":"player","data":"Android"}"#,
            format_args!(
                r#"{{"context":"protocol","data":{{"protocol_version":4,"no_broadcast":{no_broadcast},"client_id":"android-dual"}}}}"#
            ),
        )
    };

    let mut main = Client::connect(port);
    main.writer.write_all(handshake(false).as_bytes()).unwrap();
    v4_reply_contexts(&mut main, 0);

    let mut auxes: Vec<Client> = (0..3).map(|_| Client::connect(port)).collect();
    for aux in &mut auxes {
        aux.writer
            .write_all(format!("{}{V4_REQUESTS}", handshake(true)).as_bytes())
            .unwrap();
        let contexts = v4_reply_contexts(aux, 2);
        assert!(
            !contexts.iter().any(|c| c == "ping"),
            "aux was pinged: {contexts:?}"
        );
    }
    drop(auxes.pop());

    // The main socket still subscribes, so the 1 s keepalive reaches it.
    let deadline = Instant::now() + Duration::from_secs(4);
    let mut main_pinged = false;
    while Instant::now() < deadline && !main_pinged {
        main_pinged = main.read_frame()["context"] == "ping";
    }
    // An auxiliary answers on its own socket and never sees the keepalive.
    let aux = &mut auxes[0];
    aux.writer.write_all(V4_REQUESTS.as_bytes()).unwrap();
    let aux_replies: Vec<Value> = (0..2).map(|_| aux.read_frame()).collect();

    net.stop();
    assert!(main_pinged, "the main socket must survive its auxiliaries");
    assert!(
        aux_replies.iter().all(|r| r["context"] != "ping"),
        "auxiliary got a keepalive: {aux_replies:?}"
    );
}

#[test]
fn a_socket_that_trickles_bytes_without_handshaking_is_still_reaped() {
    let port = free_port();
    let net = start(Config {
        ping_interval_secs: 1,
        unhandshaked_timeout_secs: 2,
        ..Config::for_test(port)
    });
    let mut client = Client::connect(port);
    client
        .reader
        .get_ref()
        .set_read_timeout(Some(Duration::from_millis(50)))
        .unwrap();

    // One byte of an endless, never-terminated frame every 200 ms.
    let opened = Instant::now();
    let mut closed = false;
    while !closed && opened.elapsed() < Duration::from_secs(6) {
        if client.writer.write_all(b"{").is_err() {
            closed = true;
            break;
        }
        thread::sleep(Duration::from_millis(150));
        closed = client.closed_within_timeout();
    }

    net.stop();
    assert!(
        closed,
        "a trickling un-handshaked socket outlived its window"
    );
    assert!(
        opened.elapsed() < Duration::from_secs(5),
        "reaped only after {:?}",
        opened.elapsed()
    );
}
