//! End-to-end test of `mbrc send --protocol 6`: run the compiled binary against a
//! minimal V6 server (built from the same `mbrc-wire::v6` primitives) and assert it
//! completes the handshake + `ping` round-trip.

use std::io::{BufRead, BufReader, Write};
use std::net::TcpListener;
use std::process::Command;
use std::thread;

use mbrc_wire::v6;

/// Accept one connection and play the V6 server side: ack the handshake (id 0) then
/// echo the ping (id 1). Returns the two request lines it received.
fn serve_once(listener: TcpListener) -> thread::JoinHandle<Vec<String>> {
    thread::spawn(move || {
        let (stream, _) = listener.accept().expect("accept");
        let mut reader = BufReader::new(stream.try_clone().unwrap());
        let mut writer = stream;
        let mut got = Vec::new();

        // Handshake request -> success response with server_version.
        let mut hs = String::new();
        reader.read_line(&mut hs).unwrap();
        got.push(hs.trim().to_string());
        let resp = v6::response_ok(0, serde_json::json!({ "server_version": 6 }));
        writer.write_all(v6::frame_line(&resp).as_bytes()).unwrap();

        // Ping request -> echo its data back.
        let mut ping = String::new();
        reader.read_line(&mut ping).unwrap();
        got.push(ping.trim().to_string());
        let req: serde_json::Value = serde_json::from_str(ping.trim()).unwrap();
        let id = req["id"].as_u64().unwrap_or(1);
        let data = req.get("data").cloned().unwrap_or(serde_json::Value::Null);
        let pong = v6::response_ok(id, data);
        writer.write_all(v6::frame_line(&pong).as_bytes()).unwrap();

        got
    })
}

#[test]
fn send_v6_handshake_and_ping_round_trip() {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let port = listener.local_addr().unwrap().port();
    let server = serve_once(listener);

    let output = Command::new(env!("CARGO_BIN_EXE_mbrc"))
        .args([
            "send",
            "--protocol",
            "6",
            "--host",
            "127.0.0.1",
            "--port",
            &port.to_string(),
            "--op",
            "ping",
            "--json",
            r#"{"n":7}"#,
            "--wait-ms",
            "800",
        ])
        .output()
        .expect("run mbrc send");

    let stdout = String::from_utf8_lossy(&output.stdout);
    let received = server.join().unwrap();

    assert!(
        output.status.success(),
        "mbrc send exited non-zero: {output:?}"
    );
    // The server saw a well-formed handshake then a ping carrying the data.
    assert!(
        received[0].contains(r#""op":"handshake""#),
        "handshake: {}",
        received[0]
    );
    assert!(
        received[1].contains(r#""op":"ping""#),
        "ping: {}",
        received[1]
    );
    assert!(
        received[1].contains(r#""n":7"#),
        "ping data forwarded: {}",
        received[1]
    );
    // The CLI printed the server_version ack and the echoed ping response.
    assert!(stdout.contains("server_version"), "stdout:\n{stdout}");
    assert!(
        stdout.contains(r#""n":7"#),
        "echoed ping response:\n{stdout}"
    );
}
