//! `mbrc conform` - a V6 protocol conformance harness.
//!
//! Connects, completes the V6 handshake, reads the advertised `capabilities`, and
//! drives a set of protocol-invariant checks against a live server (the real
//! plugin, the test server, or a mock). Read-only by default; `--allow-writes`
//! adds a benign, restored write check. Prints a pass/fail report and exits
//! non-zero if any check fails.
//!
//! Because it is driven by `capabilities`, it stays correct as the op catalog
//! grows: every advertised op is exercised, and any op that answers `unknown_op`
//! (a lying capability) fails the run.
//!
//! It also runs a **browse value-parity differential** against the legacy V4
//! baseline: V4 and V6 read the same MusicBee library through the same FFI, so
//! their `library_*` / `browse*` totals + names/counts must agree. This validates
//! VALUES (not just shapes) with no external oracle; it is skipped when the V4
//! protocol isn't reachable on the port (e.g. a V6-only mock).

use std::collections::HashMap;
use std::io::{BufRead, BufReader, Write};
use std::net::TcpStream;
use std::process::ExitCode;
use std::time::Duration;

use serde_json::{json, Value};

use mbrc_wire::v6::{self, ClientType};
use mbrc_wire::{frame_line, parse_context, pong_frame, ClientHandshake};

use crate::args::{flag_value, has_flag};

/// Write ops (they mutate playback / tags / the queue) - skipped in the
/// capability-honesty sweep unless `--allow-writes`, since sending them with no
/// data can still fire (e.g. `player_next`).
const WRITE_OPS: &[&str] = &[
    "player_play",
    "player_pause",
    "player_play_pause",
    "player_stop",
    "player_next",
    "player_previous",
    "player_set_volume",
    "player_set_mute",
    "player_set_shuffle",
    "player_set_repeat",
    "player_set_scrobbling",
    "player_set_output",
    "now_playing_seek",
    "now_playing_set_rating",
    "now_playing_set_lfm",
    "now_playing_set_tag",
    "now_playing_list_play",
    "now_playing_list_remove",
    "now_playing_list_move",
    "now_playing_list_search",
    "now_playing_queue",
    "library_play_all",
    "playlist_play",
];

/// Known list ops whose response must be a valid `Page`.
const PAGE_OPS: &[&str] = &[
    "library_genres",
    "library_artists",
    "library_albums",
    "library_tracks",
    "library_radio",
    "playlist_list",
    "now_playing_list",
];

pub fn run(args: &[String]) -> ExitCode {
    let host = flag_value(args, "--host").unwrap_or_else(|| "127.0.0.1".to_string());
    let port: u16 = match flag_value(args, "--port")
        .as_deref()
        .unwrap_or("3000")
        .parse()
    {
        Ok(p) => p,
        Err(_) => {
            eprintln!("--port must be a number");
            return ExitCode::from(2);
        }
    };
    let allow_writes = has_flag(args, "--allow-writes");
    let timeout_ms: u64 = flag_value(args, "--wait-ms")
        .as_deref()
        .unwrap_or("3000")
        .parse()
        .unwrap_or(3000);

    let mut client = match V6Client::connect(&host, port, Duration::from_millis(timeout_ms)) {
        Ok(c) => c,
        Err(e) => {
            eprintln!("connect {host}:{port} failed: {e}");
            return ExitCode::FAILURE;
        }
    };

    println!("V6 conformance - {host}:{port}\n");
    let mut report = Report::default();
    run_checks(
        &mut client,
        &host,
        port,
        Duration::from_millis(timeout_ms),
        allow_writes,
        &mut report,
    );
    report.finish()
}

// ── the checks ───────────────────────────────────────────────────────────────

fn run_checks(
    c: &mut V6Client,
    host: &str,
    port: u16,
    timeout: Duration,
    allow_writes: bool,
    r: &mut Report,
) {
    // Handshake was validated on connect; surface the capabilities.
    let ops = c.ops.clone();
    let events = c.events_advertised.clone();
    r.pass(
        "handshake",
        format!(
            "server_version {}, {} ops, {} events",
            c.server_version,
            ops.len(),
            events.len()
        ),
    );

    // ping echoes its data.
    r.check("ping echo", || {
        let resp = c.request("ping", json!({ "probe": 42 }))?;
        let data = resp.ok()?;
        expect(data["probe"] == json!(42), "ping did not echo data")
    });

    // Correlation: fire three, read the responses out of order, each must match.
    r.check("id correlation (out-of-order)", || {
        let id_a = c.send("ping", json!({ "k": "a" }));
        let id_b = c.send("ping", json!({ "k": "b" }));
        let id_c = c.send("ping", json!({ "k": "c" }));
        for (id, k) in [(id_c, "c"), (id_a, "a"), (id_b, "b")] {
            let resp = c.recv(id)?;
            expect(resp.id == id, "response id mismatch")?;
            expect(resp.ok()?["k"] == json!(k), "out-of-order data mismatch")?;
        }
        Ok(())
    });

    // An unknown op is rejected with unknown_op (not silently handled).
    r.check("unknown op rejected", || {
        let resp = c.request("definitely_not_a_real_op", json!({}))?;
        expect(resp.err_code()? == "unknown_op", "expected unknown_op")
    });

    // Capability honesty: every advertised read op dispatches (never unknown_op).
    let read_ops: Vec<String> = ops
        .iter()
        .filter(|o| *o != "handshake" && !WRITE_OPS.contains(&o.as_str()))
        .cloned()
        .collect();
    r.check("capability honesty", || {
        for op in &read_ops {
            let resp = c.request(op, json!({}))?;
            // Success or ANY typed error is fine (the op is handled); only
            // unknown_op means the advertised capability is a lie.
            if let Ok(code) = resp.err_code() {
                expect(
                    code != "unknown_op",
                    &format!("advertised op {op} -> unknown_op"),
                )?;
            }
        }
        Ok(())
    });
    r.note("  read ops exercised", read_ops.len().to_string());

    // Page invariants for the advertised list ops.
    r.check("page invariants", || {
        for op in PAGE_OPS.iter().filter(|o| ops.iter().any(|a| a == *o)) {
            let resp = c.request(op, json!({ "offset": 0, "limit": 10 }))?;
            let Ok(data) = resp.ok() else { continue }; // an error is a different check
            let total = data["total"]
                .as_i64()
                .ok_or_else(|| format!("{op}: total not an int"))?;
            expect(data["offset"].is_i64(), &format!("{op}: offset not an int"))?;
            let items = data["items"]
                .as_array()
                .ok_or_else(|| format!("{op}: items not an array"))?;
            expect(
                total >= items.len() as i64,
                &format!("{op}: total < items.len"),
            )?;
            expect(
                items.iter().all(Value::is_object),
                &format!("{op}: non-object item"),
            )?;
        }
        Ok(())
    });

    // Canonical track schema, when tracks are actually present (skipped, not
    // failed, on an empty library / nothing playing).
    match track_schema_check(c, &ops) {
        Ok(0) => r.skip("track schema", "no tracks present"),
        Ok(n) => r.pass("track schema", format!("{n} track(s) validated")),
        Err(e) => r.fail("track schema", &e),
    }

    // A wrong-typed field yields a typed error (invalid_field), not a crash.
    r.check("typed error path", || {
        let op = PAGE_OPS
            .iter()
            .find(|o| ops.iter().any(|a| a == **o))
            .ok_or("no page op advertised to probe")?;
        let resp = c.request(op, json!({ "offset": "not-an-int" }))?;
        expect(
            resp.err_code()? == "invalid_field",
            "expected invalid_field on bad offset",
        )
    });

    // Browse VALUE parity against the legacy V4 baseline (the differential
    // oracle): V4 and V6 read the same MusicBee library through the same FFI, so
    // their browse totals + names/counts must agree. Read-only; skipped if the V4
    // protocol isn't reachable on this port (e.g. a V6-only mock server).
    browse_differential(host, port, timeout, c, r);

    // Writes + events (opt-in), with state restored so the run is repeatable.
    if allow_writes {
        writes_and_events(c, &ops, r);
    } else {
        r.skip("writes + events", "use --allow-writes");
    }
}

/// Compare V6 `library_*` browse values against the shipped V4 `browse*` baseline.
/// Both hit the same library via the same FFI callbacks, so a mismatch means V6
/// mis-reads or mis-maps the data. Compared as sorted `(name, count)` multisets so
/// ordering differences don't matter.
fn browse_differential(
    host: &str,
    port: u16,
    timeout: Duration,
    v6: &mut V6Client,
    r: &mut Report,
) {
    let mut v4 = match V4Client::connect(host, port, timeout) {
        Ok(c) => c,
        Err(e) => {
            r.skip(
                "browse value parity (V4 vs V6)",
                &format!("V4 unavailable: {e}"),
            );
            return;
        }
    };
    // (v6 op, v4 context, the per-item name key).
    const BROWSE: &[(&str, &str, &str)] = &[
        ("library_genres", "browsegenres", "genre"),
        ("library_artists", "browseartists", "artist"),
        ("library_albums", "browsealbums", "album"),
    ];
    for (v6op, v4ctx, key) in BROWSE {
        if !v6.ops.iter().any(|o| o == v6op) {
            r.skip(&format!("browse parity: {v6op}"), "op not advertised");
            continue;
        }
        r.check(&format!("browse parity: {v6op} == {v4ctx}"), || {
            let big = json!({ "offset": 0, "limit": 1_000_000 });
            let v6d = v6.request(v6op, big.clone())?.ok()?;
            let v4d = v4.query(v4ctx, big)?;
            let v6total = v6d["total"].as_i64().ok_or("v6 total not an int")?;
            let v4total = v4d["total"].as_i64().ok_or("v4 total not an int")?;
            expect(
                v6total == v4total,
                &format!("total mismatch: v6 {v6total} != v4 {v4total}"),
            )?;
            let mut a = browse_rows(&v6d["items"], key);
            let mut b = browse_rows(&v4d["data"], key);
            expect(
                a.len() == b.len(),
                &format!("item count: v6 {} != v4 {}", a.len(), b.len()),
            )?;
            a.sort();
            b.sort();
            expect(a == b, "names/counts differ from the V4 baseline")?;
            Ok(())
        });
    }
}

/// `(name, count)` pairs from a browse item array; `count` is read leniently
/// (V4 sometimes stringifies numbers).
fn browse_rows(items: &Value, key: &str) -> Vec<(String, i64)> {
    items
        .as_array()
        .into_iter()
        .flatten()
        .map(|it| {
            let name = it
                .get(key)
                .and_then(Value::as_str)
                .unwrap_or("")
                .to_string();
            let count = it
                .get("count")
                .and_then(|c| {
                    c.as_i64()
                        .or_else(|| c.as_str().and_then(|s| s.parse().ok()))
                })
                .unwrap_or(-1);
            (name, count)
        })
        .collect()
}

/// Exercise the write ops and the actively-triggerable events, **restoring state**
/// so the run is repeatable. Transient player states (volume/mute/shuffle/repeat/
/// scrobbling) are changed then restored; device/tag/last.fm writes are set to
/// their current value (shape-only, no change); genuinely destructive ops (retag,
/// track-changing transport, queue mutation, play-all) are reported as skipped
/// rather than run.
fn writes_and_events(c: &mut V6Client, ops: &[String], r: &mut Report) {
    let has = |op: &str| ops.iter().any(|o| o == op);

    let status = match c.request("player_status", json!({})).and_then(|x| x.ok()) {
        Ok(s) => s,
        Err(e) => {
            r.fail("player_status (writes setup)", &e);
            return;
        }
    };

    // volume: change it, confirm the response AND the volume_changed event, restore.
    if has("player_set_volume") {
        r.check("player_set_volume + volume_changed", || {
            let cur = status["volume"].as_i64().ok_or("no current volume")?;
            let target = (if cur >= 50 { cur - 10 } else { cur + 10 }).clamp(0, 100);
            c.clear_events();
            let resp = c.request("player_set_volume", json!({ "volume": target }))?;
            expect(
                resp.ok()?["volume"].as_i64() == Some(target),
                "volume not echoed",
            )?;
            let ev = c.wait_event("volume_changed")?;
            expect(
                ev["data"]["volume"].is_i64(),
                "volume_changed missing volume",
            )?;
            c.request("player_set_volume", json!({ "volume": cur }))?; // restore
            c.clear_events();
            Ok(())
        });
    }

    // mute: flip, confirm mute_changed, restore.
    if has("player_set_mute") {
        r.check("player_set_mute + mute_changed", || {
            let cur = status["muted"].as_bool().ok_or("no current mute")?;
            c.clear_events();
            let resp = c.request("player_set_mute", json!({ "muted": !cur }))?;
            expect(
                resp.ok()?["muted"].as_bool() == Some(!cur),
                "mute not echoed",
            )?;
            c.wait_event("mute_changed")?;
            c.request("player_set_mute", json!({ "muted": cur }))?; // restore
            c.clear_events();
            Ok(())
        });
    }

    // play/pause: toggle, confirm play_state_changed, toggle back. Only when a
    // track is loaded, so a stopped player is not started.
    if has("player_play_pause") {
        match status["play_state"].as_str() {
            Some("playing" | "paused") => r.check("player_play_pause + play_state_changed", || {
                c.clear_events();
                c.request("player_play_pause", json!({}))?;
                let ev = c.wait_event("play_state_changed")?;
                expect(
                    ev["data"]["play_state"].is_string(),
                    "event missing play_state",
                )?;
                c.request("player_play_pause", json!({}))?; // toggle back
                c.clear_events();
                Ok(())
            }),
            _ => r.skip("player_play_pause + event", "nothing loaded"),
        }
    }

    // shuffle / repeat / scrobbling: set a different value, confirm the response,
    // restore. (MusicBee emits no notification for these - no event to await.)
    restore_enum(
        c,
        r,
        &has,
        "player_set_shuffle",
        status["shuffle"].as_str(),
        "off",
        "shuffle",
    );
    restore_enum(
        c,
        r,
        &has,
        "player_set_repeat",
        status["repeat"].as_str(),
        "none",
        "all",
    );
    // Scrobbling depends on last.fm being configured, so a command failure is a
    // warning (the op is still wired), not a run failure.
    if has("player_set_scrobbling") {
        let cur = status["scrobbling"].as_bool().unwrap_or(false);
        match c.request("player_set_scrobbling", json!({ "enabled": !cur })) {
            Err(e) => r.fail("player_set_scrobbling", &e),
            Ok(resp) => match &resp.result {
                Ok(d) if d["enabled"] == json!(!cur) => {
                    let _ = c.request("player_set_scrobbling", json!({ "enabled": cur }));
                    r.pass("player_set_scrobbling (restore)", "");
                }
                Ok(_) => r.fail("player_set_scrobbling", "not echoed"),
                Err(e) if e.code == "unknown_op" => {
                    r.fail("player_set_scrobbling", "unknown_op (capability lie)")
                }
                Err(e) => r.warn(
                    "player_set_scrobbling",
                    &format!(
                        "dispatched; command returned {} (last.fm configured?)",
                        e.code
                    ),
                ),
            },
        }
    }

    // Shape-only (set to the current value - no state change): touches the audio
    // device, so confirm only that it accepts input + returns the right shape.
    if has("player_output") && has("player_set_output") {
        r.check("player_set_output (no-op)", || {
            let active = c.request("player_output", json!({}))?.ok()?["active"]
                .as_str()
                .unwrap_or("")
                .to_string();
            expect(
                c.request("player_set_output", json!({ "device": active }))?
                    .ok()?["active"]
                    .is_string(),
                "set_output shape",
            )
        });
    }

    // Now-playing writes, only when a track is playing (set to current - no change).
    let np = c
        .request("now_playing_state", json!({}))
        .and_then(|x| x.ok())
        .unwrap_or(Value::Null);
    let playing = np["track"].is_object();
    if has("now_playing_seek") {
        maybe(
            r,
            playing,
            "now_playing_seek (current pos)",
            "nothing playing",
            || {
                let pos = np["position_ms"].as_i64().unwrap_or(0);
                expect(
                    c.request("now_playing_seek", json!({ "position_ms": pos }))?
                        .ok()?["position_ms"]
                        .is_i64(),
                    "seek shape",
                )
            },
        );
    }
    if has("now_playing_set_rating") {
        maybe(
            r,
            playing,
            "now_playing_set_rating (current)",
            "nothing playing",
            || {
                let cur = np["track"]["rating"].clone();
                c.request("now_playing_set_rating", json!({ "rating": cur }))?
                    .ok()?;
                Ok(())
            },
        );
    }
    if has("now_playing_set_lfm") {
        maybe(
            r,
            playing,
            "now_playing_set_lfm (current)",
            "nothing playing",
            || {
                let cur = np["lfm_status"].as_str().unwrap_or("normal").to_string();
                expect(
                    c.request("now_playing_set_lfm", json!({ "status": cur }))?
                        .ok()?["lfm_status"]
                        .is_string(),
                    "lfm shape",
                )
            },
        );
    }

    // Ops with no safe non-mutating input (empty search/queue are invalid to
    // MusicBee; a real query/path list would mutate) and genuinely destructive ops
    // are reported, not run.
    let destructive: Vec<&str> = [
        "player_next",
        "player_previous",
        "player_stop",
        "player_play",
        "player_pause",
        "now_playing_set_tag",
        "now_playing_list_search",
        "now_playing_queue",
        "now_playing_list_play",
        "now_playing_list_remove",
        "now_playing_list_move",
        "library_play_all",
        "playlist_play",
    ]
    .into_iter()
    .filter(|op| has(op))
    .collect();
    if !destructive.is_empty() {
        r.skip(
            "destructive writes",
            &format!("not auto-run: {}", destructive.join(", ")),
        );
    }
    r.skip(
        "passive events",
        "now_playing_changed / _list_changed / _lyrics_changed / cover_cache_changed / library_changed need real changes",
    );
}

/// Set an enum player state to a different value, confirm the echo, restore.
fn restore_enum(
    c: &mut V6Client,
    r: &mut Report,
    has: &impl Fn(&str) -> bool,
    op: &str,
    cur: Option<&str>,
    a: &'static str,
    b: &'static str,
) {
    if !has(op) {
        return;
    }
    let cur = cur.unwrap_or(a).to_string();
    let other = if cur == a { b } else { a };
    r.check(&format!("{op} (restore)"), || {
        expect(
            c.request(op, json!({ "mode": other }))?.ok()?["mode"] == json!(other),
            "mode not echoed",
        )?;
        c.request(op, json!({ "mode": cur }))?;
        Ok(())
    });
}

/// Run a check only when `cond`; otherwise record a skip.
fn maybe(
    r: &mut Report,
    cond: bool,
    name: &str,
    why: &str,
    f: impl FnOnce() -> Result<(), String>,
) {
    if cond {
        r.check(name, f);
    } else {
        r.skip(name, why);
    }
}

/// Validate the canonical track schema wherever a track is present; returns the
/// number of tracks checked (0 = nothing playing / empty library).
fn track_schema_check(c: &mut V6Client, ops: &[String]) -> Result<usize, String> {
    let mut seen = 0;
    if ops.iter().any(|o| o == "now_playing_state") {
        if let Ok(d) = c.request("now_playing_state", json!({}))?.ok() {
            if d["track"].is_object() {
                check_track(&d["track"])?;
                seen += 1;
            }
        }
    }
    for op in ["library_tracks", "now_playing_list"] {
        if !ops.iter().any(|o| o == op) {
            continue;
        }
        if let Ok(d) = c.request(op, json!({ "limit": 3 }))?.ok() {
            for item in d["items"].as_array().into_iter().flatten() {
                check_track(item)?;
                seen += 1;
            }
        }
    }
    Ok(seen)
}

/// Assert a track object carries the canonical V6 schema with the right types.
fn check_track(t: &Value) -> Result<(), String> {
    expect(t["src"].is_string(), "track.src not a string")?;
    expect(t["track_no"].is_i64(), "track.track_no not an int")?;
    for f in ["year", "duration_ms"] {
        expect(
            t[f].is_i64() || t[f].is_null(),
            &format!("track.{f} not int|null"),
        )?;
    }
    expect(
        t["rating"].is_number() || t["rating"].is_null(),
        "track.rating not number|null",
    )?;
    Ok(())
}

fn expect(cond: bool, msg: &str) -> Result<(), String> {
    if cond {
        Ok(())
    } else {
        Err(msg.to_string())
    }
}

// ── the V6 client ────────────────────────────────────────────────────────────

struct V6Client {
    writer: TcpStream,
    reader: BufReader<TcpStream>,
    next_id: u64,
    pending: HashMap<u64, v6::IncomingResponse>,
    server_version: u64,
    ops: Vec<String>,
    events_advertised: Vec<String>,
    /// Unsolicited event frames seen while awaiting responses.
    events: Vec<Value>,
}

impl V6Client {
    fn connect(host: &str, port: u16, timeout: Duration) -> Result<Self, String> {
        let writer = TcpStream::connect((host, port)).map_err(|e| e.to_string())?;
        let rs = writer.try_clone().map_err(|e| e.to_string())?;
        rs.set_read_timeout(Some(timeout)).ok();
        let mut client = Self {
            writer,
            reader: BufReader::new(rs),
            next_id: 1,
            pending: HashMap::new(),
            server_version: 0,
            ops: Vec::new(),
            events_advertised: Vec::new(),
            events: Vec::new(),
        };

        // Handshake (id 0), then validate + capture the capabilities.
        let hs = v6::handshake_request("mbrc-conform", ClientType::Cli, false);
        client.write_line(&hs)?;
        let resp = client.recv(0)?;
        let data = resp.ok().map_err(|e| format!("handshake rejected: {e}"))?;
        client.server_version = data["server_version"].as_u64().unwrap_or(0);
        if client.server_version != 6 {
            return Err(format!(
                "server_version {} (expected 6)",
                client.server_version
            ));
        }
        let caps = &data["capabilities"];
        client.ops = str_vec(&caps["ops"]);
        client.events_advertised = str_vec(&caps["events"]);
        if client.ops.is_empty() {
            return Err("handshake advertised no capabilities.ops".into());
        }
        Ok(client)
    }

    fn write_line(&mut self, body: &str) -> Result<(), String> {
        self.writer
            .write_all(v6::frame_line(body).as_bytes())
            .map_err(|e| e.to_string())
    }

    /// Send a request; returns its id.
    fn send(&mut self, op: &str, data: Value) -> u64 {
        let id = self.next_id;
        self.next_id += 1;
        let _ = self.write_line(&v6::request(id, op, data));
        id
    }

    /// Read frames until the response with `id` arrives, buffering events and
    /// other-id responses.
    fn recv(&mut self, id: u64) -> Result<v6::IncomingResponse, String> {
        if let Some(r) = self.pending.remove(&id) {
            return Ok(r);
        }
        loop {
            let mut line = String::new();
            let n = self
                .reader
                .read_line(&mut line)
                .map_err(|e| e.to_string())?;
            if n == 0 {
                return Err(format!("connection closed while awaiting id {id}"));
            }
            let line = line.trim();
            if line.is_empty() {
                continue;
            }
            match v6::parse_response(line) {
                Some(resp) if resp.id == id => return Ok(resp),
                Some(resp) => {
                    self.pending.insert(resp.id, resp);
                }
                None => self.buffer_event(line),
            }
        }
    }

    /// Send + await one op.
    fn request(&mut self, op: &str, data: Value) -> Result<v6::IncomingResponse, String> {
        let id = self.send(op, data);
        self.recv(id)
    }

    /// Buffer a non-response frame if it is an event.
    fn buffer_event(&mut self, line: &str) {
        if let Ok(v) = serde_json::from_str::<Value>(line) {
            if v.get("kind").and_then(Value::as_str) == Some("event") {
                self.events.push(v);
            }
        }
    }

    /// Drop buffered events (call before triggering a change, so a stale event
    /// can't be mistaken for the one under test).
    fn clear_events(&mut self) {
        self.events.clear();
    }

    /// Wait for an event named `name` (checking the buffer first, then reading
    /// frames until it arrives or the socket read times out). Responses seen along
    /// the way are buffered by id; other events are buffered.
    fn wait_event(&mut self, name: &str) -> Result<Value, String> {
        if let Some(pos) = self.events.iter().position(|e| e["event"] == json!(name)) {
            return Ok(self.events.remove(pos));
        }
        loop {
            let mut line = String::new();
            match self.reader.read_line(&mut line) {
                Ok(0) => return Err(format!("connection closed waiting for event {name}")),
                Ok(_) => {}
                Err(_) => return Err(format!("timed out waiting for event {name}")),
            }
            let line = line.trim();
            if line.is_empty() {
                continue;
            }
            if let Some(resp) = v6::parse_response(line) {
                self.pending.insert(resp.id, resp);
                continue;
            }
            if let Ok(v) = serde_json::from_str::<Value>(line) {
                if v.get("kind").and_then(Value::as_str) == Some("event") {
                    if v["event"] == json!(name) {
                        return Ok(v);
                    }
                    self.events.push(v);
                }
            }
        }
    }
}

/// A minimal legacy V4 client for the browse differential: complete the V4
/// handshake, then send a `{context,data}` command and return the matching
/// reply's `data`. Only what the value-parity check needs - not a full client.
struct V4Client {
    writer: TcpStream,
    reader: BufReader<TcpStream>,
}

impl V4Client {
    fn connect(host: &str, port: u16, timeout: Duration) -> Result<Self, String> {
        let writer = TcpStream::connect((host, port)).map_err(|e| e.to_string())?;
        let rs = writer.try_clone().map_err(|e| e.to_string())?;
        rs.set_read_timeout(Some(timeout)).ok();
        let mut c = Self {
            writer,
            reader: BufReader::new(rs),
        };
        // V4 handshake (player -> protocol), answered via the shared
        // ClientHandshake until the server's `protocol` reply lands.
        let mut hs = ClientHandshake::new("Android", 4, false);
        c.write_line(&hs.initial())?;
        loop {
            let line = c.read_line()?;
            let ctx = parse_context(&line).unwrap_or_default();
            if let Some(reply) = hs.on_incoming(&ctx) {
                c.write_line(&reply)?;
            }
            if ctx == "protocol" {
                break;
            }
        }
        Ok(c)
    }

    fn write_line(&mut self, body: &str) -> Result<(), String> {
        self.writer
            .write_all(frame_line(body).as_bytes())
            .map_err(|e| e.to_string())
    }

    fn read_line(&mut self) -> Result<String, String> {
        loop {
            let mut line = String::new();
            let n = self
                .reader
                .read_line(&mut line)
                .map_err(|e| e.to_string())?;
            if n == 0 {
                return Err("connection closed".into());
            }
            let t = line.trim();
            if !t.is_empty() {
                return Ok(t.to_string());
            }
        }
    }

    /// Send a `{context,data}` command; return the `data` of the matching reply.
    /// Answers keepalive pings and skips unrelated broadcasts.
    fn query(&mut self, context: &str, data: Value) -> Result<Value, String> {
        let frame = json!({ "context": context, "data": data }).to_string();
        self.write_line(&frame)?;
        loop {
            let line = self.read_line()?;
            match parse_context(&line).as_deref() {
                Some("ping") => {
                    let _ = self.write_line(&pong_frame());
                }
                Some(ctx) if ctx == context => {
                    let v: Value = serde_json::from_str(&line).map_err(|e| e.to_string())?;
                    return Ok(v["data"].clone());
                }
                _ => {} // unrelated broadcast; keep reading
            }
        }
    }
}

/// Convenience accessors over a parsed response. (`parse_response` already
/// enforced the envelope: `kind == "response"`, an `id`, and `data` XOR `error`.)
trait RespExt {
    fn ok(&self) -> Result<Value, String>;
    fn err_code(&self) -> Result<String, String>;
}

impl RespExt for v6::IncomingResponse {
    fn ok(&self) -> Result<Value, String> {
        match &self.result {
            Ok(data) => Ok(data.clone()),
            Err(e) => Err(format!("{}: {}", e.code, e.message)),
        }
    }
    fn err_code(&self) -> Result<String, String> {
        match &self.result {
            Err(e) => Ok(e.code.clone()),
            Ok(_) => Err("expected an error, got success".into()),
        }
    }
}

fn str_vec(v: &Value) -> Vec<String> {
    v.as_array()
        .into_iter()
        .flatten()
        .filter_map(|x| x.as_str().map(String::from))
        .collect()
}

// ── the report ───────────────────────────────────────────────────────────────

#[derive(Default)]
struct Report {
    failures: usize,
    checks: usize,
}

impl Report {
    fn pass(&mut self, name: &str, detail: impl Into<String>) {
        self.checks += 1;
        println!("  ok   {name:<28} {}", detail.into());
    }
    fn fail(&mut self, name: &str, msg: &str) {
        self.checks += 1;
        self.failures += 1;
        println!("  FAIL {name:<28} {msg}");
    }
    fn skip(&mut self, name: &str, why: &str) {
        println!("  --   {name:<28} skipped ({why})");
    }
    /// A dispatched op whose underlying command failed for environmental reasons
    /// (unconfigured feature, MusicBee state). Visible but not a run failure - the
    /// protocol layer behaved correctly (a typed error, not a crash).
    fn warn(&mut self, name: &str, msg: &str) {
        self.checks += 1;
        println!("  warn {name:<28} {msg}");
    }
    fn note(&self, name: &str, detail: String) {
        println!("       {name:<26} {detail}");
    }
    /// Run a fallible check and record pass/fail.
    fn check(&mut self, name: &str, f: impl FnOnce() -> Result<(), String>) {
        match f() {
            Ok(()) => self.pass(name, ""),
            Err(e) => self.fail(name, &e),
        }
    }
    fn finish(self) -> ExitCode {
        println!("\n{} checks, {} failures", self.checks, self.failures);
        if self.failures == 0 {
            ExitCode::SUCCESS
        } else {
            ExitCode::FAILURE
        }
    }
}
