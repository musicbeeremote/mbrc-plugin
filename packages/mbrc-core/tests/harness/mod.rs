//! Socket plumbing shared by the integration tests: a server on a free loopback
//! port, and a line-framed client to talk to it.

#![allow(dead_code)]

use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::sync::Arc;
use std::time::{Duration, Instant};

use serde_json::Value;

use mbrc_core::config::Config;
use mbrc_core::providers::NullProviders;
use mbrc_core::server;
use mbrc_core::state::Core;
use mbrc_wire::v6::{self, ClientType};
use mbrc_wire::{frame_line, player_frame, protocol_frame};

/// How many ports to try before giving up on finding a free one.
const BIND_ATTEMPTS: u32 = 16;

/// Grabs a currently-free port by binding an ephemeral socket and releasing it.
fn free_port() -> u16 {
    std::net::TcpListener::bind("127.0.0.1:0")
        .unwrap()
        .local_addr()
        .unwrap()
        .port()
}

/// Starts the core `make` builds for a port nothing else holds, and says which.
///
/// The port has to be released before the server can take it, and the tests run
/// in parallel, so between asking the OS for a free one and binding it another
/// test can be handed the same number. Rolling a new port and trying again is
/// what closes that window: the alternative is a suite that fails on a race
/// nobody introduced, roughly one Windows run in ten.
pub fn start_core(make: impl Fn(u16) -> Arc<Core>) -> (u16, Arc<Core>, server::NetHandle) {
    for _ in 0..BIND_ATTEMPTS {
        let port = free_port();
        let core = make(port);
        match server::start(core.clone()) {
            Ok(net) => return (port, core, net),
            Err(e) if e.kind() == std::io::ErrorKind::AddrInUse => continue,
            Err(e) => panic!("server should bind and start: {e:?}"),
        }
    }
    panic!("no free port after {BIND_ATTEMPTS} attempts");
}

/// [`start_core`] for a [`NullProviders`] core built from `config`.
pub fn start_on_free_port(config: impl Fn(u16) -> Config) -> (u16, server::NetHandle) {
    let (port, _, net) = start_core(|port| null_core(config(port)));
    (port, net)
}

/// [`start_on_free_port`] with the test config.
pub fn start() -> (u16, server::NetHandle) {
    start_on_free_port(Config::for_test)
}

/// [`start`], also handing back the core for a test that drives it directly.
pub fn start_with_core() -> (u16, Arc<Core>, server::NetHandle) {
    start_core(|port| null_core(Config::for_test(port)))
}

pub fn null_core(config: Config) -> Arc<Core> {
    Arc::new(Core::new(Arc::new(NullProviders), config))
}

/// Connects to the server, returning the write half and a buffered read half
/// that gives up after `timeout`.
pub fn connect(port: u16, timeout: Duration) -> (TcpStream, BufReader<TcpStream>) {
    let writer = TcpStream::connect(("127.0.0.1", port)).expect("connect");
    let read = writer.try_clone().unwrap();
    read.set_read_timeout(Some(timeout)).unwrap();
    (writer, BufReader::new(read))
}

/// Reads one line-framed JSON frame.
pub fn read_frame(reader: &mut BufReader<TcpStream>) -> Value {
    let mut line = String::new();
    reader.read_line(&mut line).expect("read frame");
    serde_json::from_str(line.trim()).unwrap_or_else(|e| panic!("not JSON ({e}): {line:?}"))
}

/// Sends `line` with the V6 `\n` terminator.
pub fn send(writer: &mut TcpStream, line: &str) {
    writer.write_all(v6::frame_line(line).as_bytes()).unwrap();
}

/// Sends `request` and reads the frame that answers it.
pub fn round_trip(
    writer: &mut TcpStream,
    reader: &mut BufReader<TcpStream>,
    request: &str,
) -> Value {
    send(writer, request);
    read_frame(reader)
}

/// A V6 handshake request from a `cli` client.
pub fn v6_handshake(client_id: &str, no_broadcast: bool) -> String {
    v6::handshake_request(client_id, ClientType::Cli, no_broadcast)
}

/// The `ops` or `events` a V6 handshake reply advertises.
pub fn advertised<'a>(reply: &'a Value, kind: &str) -> Vec<&'a str> {
    reply["data"]["capabilities"][kind]
        .as_array()
        .unwrap()
        .iter()
        .filter_map(|v| v.as_str())
        .collect()
}

/// The V4 `player` + `protocol` handshake frames, CRLF-terminated.
pub fn v4_handshake_frames(player: &str, no_broadcast: bool) -> String {
    frame_line(&player_frame(player)) + &frame_line(&protocol_frame(4, no_broadcast))
}

/// Sends V4 handshake `frames` and reads past their two replies.
pub fn v4_handshake(writer: &mut TcpStream, reader: &mut BufReader<TcpStream>, frames: &str) {
    writer.write_all(frames.as_bytes()).unwrap();
    for _ in 0..2 {
        let mut line = String::new();
        reader.read_line(&mut line).expect("handshake reply");
    }
}

/// Reads until EOF or the read timeout; true when the server closed.
pub fn closed_within_timeout(reader: &mut BufReader<TcpStream>) -> bool {
    loop {
        let mut line = String::new();
        match reader.read_line(&mut line) {
            Ok(0) => return true,
            Ok(_) => {}
            Err(_) => return false,
        }
    }
}

/// Listens for `window` without sending, returning `(saw_ping, closed)`.
///
/// Read timeouts inside the window are waited out, so the reader's own timeout
/// only sets how often the window is checked.
pub fn listen_silently(reader: &mut BufReader<TcpStream>, window: Duration) -> (bool, bool) {
    let started = Instant::now();
    let mut saw_ping = false;
    while started.elapsed() < window {
        let mut line = String::new();
        match reader.read_line(&mut line) {
            Ok(0) => return (saw_ping, true),
            Ok(_) => saw_ping |= line.contains("\"ping\""),
            Err(_) => {}
        }
    }
    (saw_ping, false)
}
