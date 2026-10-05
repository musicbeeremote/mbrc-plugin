//! `mbrc conform` - a V6 protocol conformance harness.
//!
//! Connects, completes the V6 handshake, reads the advertised `capabilities`, and
//! drives a set of protocol-invariant checks against a live server (the real
//! plugin, the test server, or a mock). Read-only by default. `--allow-writes`
//! also changes player settings, the queue and a playlist of its own, and puts
//! each back; it never writes tags or ratings or starts playback. Prints a
//! pass/fail report and exits non-zero if any check fails.
//!
//! Because it is driven by `capabilities`, it stays correct as the op catalog
//! grows: every advertised read op is exercised, and any op that answers `unknown_op`
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

use serde_json::{Value, json};

use mbrc_wire::v6::{self, ClientType};
use mbrc_wire::{ClientHandshake, frame_line, parse_context, pong_frame};

use crate::args::{flag_value, has_flag, run_client_id};

/// Whether the capability sweep may send `op` with no data.
///
/// Only a read: sending a write with empty data can still fire, and
/// `library_queue {}` queues the whole library. The read ops are the V6
/// fuzzer's table, which a test holds equal to the core's own permission map,
/// so an op missing from it is never sent rather than sent by mistake.
fn safe_to_sweep(op: &str) -> bool {
    matches!(op, "ping" | "pair") || crate::fuzz::v6::ops::SPECS.iter().any(|s| s.op == op)
}

/// Known list ops whose response must be a valid `Page`.
/// The server's page size when a request names no `limit`; a page that came
/// back larger means the default is not being applied.
const DEFAULT_PAGE_LIMIT: usize = 1000;

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

    r.check("ping echo", || {
        let resp = c.request("ping", json!({ "probe": 42 }))?;
        let data = resp.ok()?;
        expect(data["probe"] == json!(42), "ping did not echo data")
    });

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

    r.check("unknown op rejected", || {
        let resp = c.request("definitely_not_a_real_op", json!({}))?;
        expect(resp.err_code()? == "unknown_op", "expected unknown_op")
    });

    // Every advertised read op must dispatch: only `unknown_op` is a lie.
    let read_ops: Vec<String> = ops.iter().filter(|o| safe_to_sweep(o)).cloned().collect();
    r.check("capability honesty", || {
        for op in &read_ops {
            let resp = c.request(op, json!({}))?;
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

    protocol_surface(c, host, port, timeout, &ops, r);
    list_contract(c, &ops, allow_writes, r);
    library_sync_contract(c, &ops, r);

    browse_differential(host, port, timeout, c, r);

    // Writes + events (opt-in), with state restored so the run is repeatable.
    if allow_writes {
        writes_and_events(c, &ops, r);
    } else {
        r.skip("writes + events", "use --allow-writes");
    }
}

/// The checks that are about the protocol's own contracts rather than the op
/// catalog: which errors name a field, what an absent `limit` means, and the
/// identity and versioning guards a client has to honour.
fn protocol_surface(
    c: &mut V6Client,
    host: &str,
    port: u16,
    timeout: Duration,
    ops: &[String],
    r: &mut Report,
) {
    r.check("error names its field", || {
        let op = PAGE_OPS
            .iter()
            .find(|o| ops.iter().any(|a| a == **o))
            .ok_or("no page op advertised to probe")?;
        let bad = c.request(op, json!({ "offset": "not-an-int" }))?;
        expect(
            bad.err_field()?.as_deref() == Some("offset"),
            "invalid_field did not name `offset`",
        )?;
        let unknown = c.request("definitely_not_a_real_op", json!({}))?;
        expect(
            unknown.err_field()?.is_none(),
            "unknown_op named a field it is not about",
        )
    });

    r.check("absent limit is bounded", || {
        for op in PAGE_OPS.iter().filter(|o| ops.iter().any(|a| a == *o)) {
            let data = c.request(op, json!({}))?.ok()?;
            let items = data["items"]
                .as_array()
                .ok_or_else(|| format!("{op}: items not an array"))?;
            expect(
                items.len() <= DEFAULT_PAGE_LIMIT,
                &format!("{op}: {} items with no limit set", items.len()),
            )?;
        }
        Ok(())
    });

    r.check("client_token issued then demanded", || {
        let id = run_client_id("mbrc-conform-token");
        let (_first, resp) = V6Client::open(host, port, timeout, &id, None)?;
        let token = resp.ok()?["client_token"]
            .as_str()
            .ok_or("first contact was issued no client_token")?
            .to_owned();

        let (_known, resp) = V6Client::open(host, port, timeout, &id, Some(&token))?;
        expect(
            resp.ok()?.get("client_token").is_none(),
            "a client that presented its token was issued another",
        )?;

        let (_clone, resp) = V6Client::open(host, port, timeout, &id, None)?;
        expect(
            resp.err_code()? == "invalid_token",
            "a second install claiming the id was not refused",
        )?;
        expect(
            resp.err_field()?.as_deref() == Some("client_token"),
            "invalid_token did not name `client_token`",
        )
    });
}

/// `library_changes`: a full read pages to its end, its cursor answers with
/// nothing older than itself, and a cursor from another library is told to resync.
fn library_sync_contract(c: &mut V6Client, ops: &[String], r: &mut Report) {
    if !ops.iter().any(|o| o == "library_changes") {
        r.skip("library sync", "library_changes not advertised");
        return;
    }
    let first = match c.request("library_changes", json!({ "limit": 2 })) {
        Ok(resp) => match resp.err_code() {
            Ok(code) if code == "unavailable" => {
                r.skip("library sync", "the library index is still being built");
                return;
            }
            _ => resp,
        },
        Err(e) => return r.fail("library sync", &e),
    };
    r.check("library sync", || {
        let data = first.ok()?;
        expect(data["epoch"].is_string(), "epoch not a string")?;
        expect(
            data["generation"].is_u64(),
            "generation not an unsigned int",
        )?;
        expect(
            data["resync"] == json!(false),
            "a first read asked to resync",
        )?;
        expect(data["total"].is_u64(), "total not an unsigned int")?;
        let data_total = data["total"].clone();
        let cursor = json!({ "epoch": data["epoch"], "generation": data["generation"] });

        let mut page = data;
        let mut pages = 1;
        while !page["next"].is_null() && pages < 3 {
            for item in page["items"].as_array().ok_or("items not an array")? {
                expect(
                    item["change"] == json!("upsert"),
                    "a first read served a delete",
                )?;
                check_track(&item["track"])?;
            }
            let resp = c.request(
                "library_changes",
                json!({ "limit": 2, "after": page["next"] }),
            )?;
            page = resp.ok()?;
            expect(
                page["epoch"] == cursor["epoch"],
                "epoch moved between pages",
            )?;
            expect(page["total"] == data_total, "total moved between pages")?;
            expect(
                page["generation"] == cursor["generation"],
                "cursor moved between pages",
            )?;
            pages += 1;
        }

        let since = c
            .request("library_changes", json!({ "since": cursor }))?
            .ok()?;
        expect(
            since["resync"] == json!(false),
            "its own cursor asked to resync",
        )?;
        expect(
            since["generation"].as_u64() >= cursor["generation"].as_u64(),
            "the log went backwards",
        )?;

        let foreign = json!({ "epoch": "not-this-library", "generation": 0 });
        let other = c
            .request("library_changes", json!({ "since": foreign }))?
            .ok()?;
        expect(
            other["resync"] == json!(true),
            "a foreign epoch was answered",
        )
    });
}

/// The now-playing list's own contract: what an item carries, what a client
/// has to ask for, and what happens to a mutation whose `version` has moved on.
fn list_contract(c: &mut V6Client, ops: &[String], allow_writes: bool, r: &mut Report) {
    if ops.iter().any(|o| o == "now_playing_list") {
        r.check("list item keys", || {
            let data = c
                .request("now_playing_list", json!({ "offset": 0, "limit": 20 }))?
                .ok()?;
            expect(data["version"].is_i64(), "list carries no version")?;
            for (rank, item) in data["items"].as_array().into_iter().flatten().enumerate() {
                expect(item["order"].is_i64(), "item has no order")?;
                expect(
                    item["position"] == json!(rank as i64),
                    "position is not the 0-based rank within the page",
                )?;
                expect(item["play_position"].is_i64(), "item has no play_position")?;
            }
            Ok(())
        });
    } else {
        r.skip("list item keys", "now_playing_list not advertised");
    }

    if ops.iter().any(|o| o == "now_playing_state") {
        r.check("list_order is opt-in", || {
            let off = c.request("now_playing_state", json!({}))?.ok()?;
            expect(
                off["list_order"].is_null(),
                "list_order was returned without being asked for",
            )?;
            let on = c
                .request("now_playing_state", json!({ "include_list_order": true }))?
                .ok()?;
            expect(
                on["list_order"].is_i64() || on["list_order"].is_null(),
                "list_order is not an int",
            )
        });
    } else {
        r.skip("list_order is opt-in", "now_playing_state not advertised");
    }

    if allow_writes && ops.iter().any(|o| o == "now_playing_list_move") {
        r.check("stale version rejected", || {
            let before = c
                .request("now_playing_list", json!({ "offset": 0, "limit": 2 }))?
                .ok()?;
            let version = before["version"]
                .as_i64()
                .ok_or("list carries no version")?;
            let items = before["items"].as_array().cloned().unwrap_or_default();
            if items.len() < 2 {
                return Ok(());
            }
            let from = items[0]["order"].as_i64().ok_or("item has no order")?;
            let to = items[1]["order"].as_i64().ok_or("item has no order")?;
            let resp = c.request(
                "now_playing_list_move",
                json!({ "from": from, "to": to, "version": version - 1 }),
            )?;
            let code = resp.err_code()?;
            expect(
                code == "stale_list",
                &format!("a move against a stale version answered {code}, not stale_list"),
            )?;
            let after = c
                .request("now_playing_list", json!({ "offset": 0, "limit": 2 }))?
                .ok()?;
            expect(
                after["version"] == json!(version),
                "a rejected move still bumped the version",
            )
        });
    } else {
        r.skip("stale version rejected", "needs --allow-writes");
    }
}

/// Compare V6 `library_*` browse values against the shipped V4 `browse*` baseline.
/// Both hit the same library via the same FFI callbacks, so a mismatch means V6
/// misreads or mismaps the data. Compared as sorted `(name, count)` multisets so
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

/// Exercise the write ops and the events they trigger, **restoring state** so
/// the run is repeatable and leaves the server as it found it.
///
/// Player settings are changed and put back. The queue and playlists are only
/// touched through tracks this run appends and a playlist it creates, which it
/// removes again. Nothing writes tags or ratings, calls last.fm, changes the
/// output device or starts playback. A final check compares the state with the
/// snapshot taken first.
fn writes_and_events(c: &mut V6Client, ops: &[String], r: &mut Report) {
    let has = |op: &str| ops.iter().any(|o| o == op);

    let status = match c.request("player_status", json!({})).and_then(|x| x.ok()) {
        Ok(s) => s,
        Err(e) => {
            r.fail("player_status (writes setup)", &e);
            return;
        }
    };
    let before = match Snapshot::take(c) {
        Ok(s) => s,
        Err(e) => {
            r.fail("state snapshot (writes setup)", &e);
            return;
        }
    };
    let fixtures = fixture_tracks(c);

    player_writes(c, r, &has, &status);
    now_playing_writes(c, r, &has);
    match &fixtures {
        Ok(paths) => {
            queue_writes(c, r, &has, paths);
            playlist_writes(c, r, &has, paths);
        }
        Err(e) => r.skip("queue + playlist writes", e),
    }
    report_unrun_writes(r, &has);

    r.check("state restored", || {
        let after = Snapshot::take(c)?;
        before.compare(&after)
    });
}

/// What a write run must leave as it found it.
struct Snapshot {
    player: Value,
    queue: Vec<String>,
    playlists: Vec<String>,
}

impl Snapshot {
    fn take(c: &mut V6Client) -> Result<Self, String> {
        let mut player = c.request("player_status", json!({}))?.ok()?;
        // Playback moves on its own; only the settings a run changes are compared.
        player["play_state"] = Value::Null;
        Ok(Self {
            player,
            queue: queue_paths(c)?,
            playlists: playlist_names(c)?,
        })
    }

    fn compare(&self, after: &Snapshot) -> Result<(), String> {
        expect(
            self.player == after.player,
            &format!(
                "player settings changed: {} -> {}",
                self.player, after.player
            ),
        )?;
        expect(
            self.queue == after.queue,
            &format!(
                "queue changed: {} -> {} entries",
                self.queue.len(),
                after.queue.len()
            ),
        )?;
        expect(
            self.playlists == after.playlists,
            &format!(
                "playlists changed: {:?} -> {:?}",
                self.playlists, after.playlists
            ),
        )
    }
}

/// Every `src` in the queue, in queue order.
fn queue_paths(c: &mut V6Client) -> Result<Vec<String>, String> {
    let data = c
        .request("now_playing_list", json!({ "offset": 0, "limit": 0 }))?
        .ok()?;
    Ok(srcs(&data["items"]))
}

fn playlist_names(c: &mut V6Client) -> Result<Vec<String>, String> {
    let data = c
        .request("playlist_list", json!({ "offset": 0, "limit": 0 }))?
        .ok()?;
    let mut names: Vec<String> = data["items"]
        .as_array()
        .into_iter()
        .flatten()
        .filter_map(|p| p["name"].as_str().map(String::from))
        .collect();
    names.sort();
    Ok(names)
}

fn srcs(items: &Value) -> Vec<String> {
    items
        .as_array()
        .into_iter()
        .flatten()
        .filter_map(|t| t["src"].as_str().map(String::from))
        .collect()
}

/// The library tracks a write run queues and lists: the first three, so two
/// runs use the same ones.
fn fixture_tracks(c: &mut V6Client) -> Result<Vec<String>, String> {
    let data = c
        .request("library_tracks", json!({ "offset": 0, "limit": 3 }))?
        .ok()?;
    let paths = srcs(&data["items"]);
    expect(paths.len() == 3, "the library holds fewer than 3 tracks")?;
    Ok(paths)
}

/// The name of the playlist a write run creates, and deletes again.
const FIXTURE_PLAYLIST: &str = "mbrc-conform";

/// The player-control writes, each restored to the value `status` reported.
fn player_writes(c: &mut V6Client, r: &mut Report, has: &impl Fn(&str) -> bool, status: &Value) {
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

    player_mode_writes(c, r, has, status);
}

/// The player modes: set a different value, confirm the echo and the event,
/// put it back.
fn player_mode_writes(
    c: &mut V6Client,
    r: &mut Report,
    has: &impl Fn(&str) -> bool,
    status: &Value,
) {
    restore_enum(
        c,
        r,
        &has,
        ("player_set_shuffle", "shuffle_changed"),
        status["shuffle"].as_str(),
        "off",
        "shuffle",
    );
    restore_enum(
        c,
        r,
        &has,
        ("player_set_repeat", "repeat_changed"),
        status["repeat"].as_str(),
        "none",
        "all",
    );
    if has("player_set_stop_after_current") {
        r.check("player_set_stop_after_current + event (restore)", || {
            let cur = status["stop_after_current"]
                .as_bool()
                .ok_or("no current stop_after_current")?;
            c.clear_events();
            let resp = c.request("player_set_stop_after_current", json!({ "enabled": !cur }))?;
            expect(resp.ok()?["enabled"] == json!(!cur), "not echoed")?;
            c.wait_event("stop_after_current_changed")?;
            c.request("player_set_stop_after_current", json!({ "enabled": cur }))?;
            c.clear_events();
            Ok(())
        });
    }
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
}

/// The now-playing writes a run can make without touching the library: a seek
/// to where playback already is.
fn now_playing_writes(c: &mut V6Client, r: &mut Report, has: &impl Fn(&str) -> bool) {
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
}

/// Queue writes on tracks this run appends: queue, move, a stale move, a scoped
/// `library_queue`, then remove exactly what was added. The user's own entries
/// keep their slots throughout, so nothing has to be rebuilt.
fn queue_writes(
    c: &mut V6Client,
    r: &mut Report,
    has: &impl Fn(&str) -> bool,
    fixtures: &[String],
) {
    let needed = [
        "now_playing_queue",
        "now_playing_list_move",
        "now_playing_list_remove",
    ];
    if !needed.iter().all(|op| has(op)) {
        r.skip("queue writes", "queue ops not advertised");
        return;
    }
    let start = match queue_total(c) {
        Ok(n) => n,
        Err(e) => return r.fail("queue writes", &e),
    };

    r.check("now_playing_queue appends in order", || {
        c.request(
            "now_playing_queue",
            json!({ "paths": fixtures, "mode": "last" }),
        )?
        .ok()?;
        wait_for_queue(c, start + fixtures.len())?;
        let (_, tail) = queue_tail(c, start)?;
        expect(
            tail == fixtures,
            &format!("appended {tail:?}, not {fixtures:?}"),
        )
    });

    r.check("now_playing_list_move + stale version", || {
        let (version, _) = queue_tail(c, start)?;
        c.request(
            "now_playing_list_move",
            json!({ "from": start, "to": start + 2, "version": version }),
        )?
        .ok()?;
        let (_, tail) = queue_tail(c, start)?;
        let moved = vec![
            fixtures[1].clone(),
            fixtures[2].clone(),
            fixtures[0].clone(),
        ];
        expect(
            tail == moved,
            &format!("after the move {tail:?}, not {moved:?}"),
        )?;
        let stale = c.request(
            "now_playing_list_move",
            json!({ "from": start, "to": start + 1, "version": version }),
        )?;
        expect(
            stale.err_code()? == "stale_list",
            "a move on the version before the last one was not stale_list",
        )
    });

    if has("library_queue") {
        r.check("library_queue (one album, last)", || {
            let (album, artist, count) = small_album(c)?;
            let before = queue_total(c)?;
            let resp = c
                .request(
                    "library_queue",
                    json!({ "album": album, "artist": artist, "mode": "last" }),
                )?
                .ok()?;
            let queued = resp["count"].as_u64().ok_or("count not an int")? as usize;
            expect(
                queued == count,
                &format!("queued {queued}, the album holds {count}"),
            )?;
            wait_for_queue(c, before + queued)
        });
    }

    r.check("now_playing_list_remove (what this run added)", || {
        remove_appended(c, start)?;
        wait_for_queue(c, start)
    });
    // A check above may have stopped half way; never leave its tracks behind.
    if queue_total(c).is_ok_and(|n| n > start) {
        let _ = remove_appended(c, start);
        let _ = wait_for_queue(c, start);
    }
}

fn queue_total(c: &mut V6Client) -> Result<usize, String> {
    let data = c
        .request("now_playing_list", json!({ "offset": 0, "limit": 1 }))?
        .ok()?;
    data["total"]
        .as_u64()
        .map(|n| n as usize)
        .ok_or_else(|| "total not an int".to_string())
}

/// The queue's version and the `src`s from slot `start` to the end.
fn queue_tail(c: &mut V6Client, start: usize) -> Result<(i64, Vec<String>), String> {
    let data = c
        .request("now_playing_list", json!({ "offset": start, "limit": 0 }))?
        .ok()?;
    let version = data["version"].as_i64().ok_or("no version")?;
    Ok((version, srcs(&data["items"])))
}

/// Waits for the queue to reach `total`: MusicBee applies a queue request after
/// answering it.
fn wait_for_queue(c: &mut V6Client, total: usize) -> Result<(), String> {
    for _ in 0..60 {
        if queue_total(c)? == total {
            return Ok(());
        }
        std::thread::sleep(Duration::from_millis(50));
    }
    Err(format!("the queue never reached {total} entries"))
}

/// Removes every slot from `start` to the end, guarded by the current version.
fn remove_appended(c: &mut V6Client, start: usize) -> Result<(), String> {
    let total = queue_total(c)?;
    if total <= start {
        return Ok(());
    }
    let (version, _) = queue_tail(c, start)?;
    let orders: Vec<usize> = (start..total).collect();
    c.request(
        "now_playing_list_remove",
        json!({ "orders": orders, "version": version }),
    )?
    .ok()
    .map(|_| ())
}

/// An album of 1 to 5 tracks, the first such in the album list.
fn small_album(c: &mut V6Client) -> Result<(String, String, usize), String> {
    let data = c
        .request("library_albums", json!({ "offset": 0, "limit": 200 }))?
        .ok()?;
    data["items"]
        .as_array()
        .into_iter()
        .flatten()
        .find_map(|a| {
            let count = a["count"].as_u64()? as usize;
            let album = a["album"].as_str().filter(|n| !n.is_empty())?;
            (1..=5).contains(&count).then(|| {
                (
                    album.to_string(),
                    a["artist"].as_str().unwrap_or("").to_string(),
                    count,
                )
            })
        })
        .ok_or_else(|| "no album of 1 to 5 tracks in the first 200".to_string())
}

/// Playlist writes on a playlist this run creates and deletes: create, add,
/// move, a stale edit, remove, set, delete.
fn playlist_writes(
    c: &mut V6Client,
    r: &mut Report,
    has: &impl Fn(&str) -> bool,
    fixtures: &[String],
) {
    let needed = [
        "playlist_create",
        "playlist_add_tracks",
        "playlist_move_tracks",
        "playlist_remove_tracks",
        "playlist_set_tracks",
        "playlist_delete",
        "playlist_tracks",
    ];
    if !needed.iter().all(|op| has(op)) {
        r.skip("playlist writes", "playlist edit ops not advertised");
        return;
    }
    // A run that died before its delete left its playlist; take it first.
    for url in fixture_playlist_urls(c).unwrap_or_default() {
        let _ = c.request("playlist_delete", json!({ "url": url }));
    }

    let mut url = String::new();
    r.check("playlist edits on its own playlist", || {
        let made = c
            .request(
                "playlist_create",
                json!({ "name": FIXTURE_PLAYLIST, "paths": &fixtures[..2] }),
            )?
            .ok()?;
        url = made["url"].as_str().ok_or("no url")?.to_string();
        let tracks = |c: &mut V6Client, url: &str| -> Result<Vec<String>, String> {
            let data = c.request("playlist_tracks", json!({ "url": url }))?.ok()?;
            Ok(srcs(&data["items"]))
        };
        let pick = |order: &[usize]| -> Vec<String> {
            order.iter().map(|&i| fixtures[i].clone()).collect()
        };
        expect(tracks(c, &url)? == pick(&[0, 1]), "after create")?;

        let added = c
            .request(
                "playlist_add_tracks",
                json!({ "url": url, "paths": [&fixtures[2]], "version": made["version"] }),
            )?
            .ok()?;
        expect(added["added"] == json!(1), "added is not 1")?;
        expect(tracks(c, &url)? == pick(&[0, 1, 2]), "after add")?;

        let stale_version = added["version"].clone();
        let moved = c
            .request(
                "playlist_move_tracks",
                json!({ "url": url, "from_orders": [0], "to_order": 2, "version": added["version"] }),
            )?
            .ok()?;
        expect(tracks(c, &url)? == pick(&[1, 2, 0]), "after move")?;

        let stale = c.request(
            "playlist_remove_tracks",
            json!({ "url": url, "orders": [0], "version": stale_version }),
        )?;
        expect(
            stale.err_code()? == "stale_list",
            "an edit on an old version was not stale_list",
        )?;

        let removed = c
            .request(
                "playlist_remove_tracks",
                json!({ "url": url, "orders": [0], "version": moved["version"] }),
            )?
            .ok()?;
        expect(tracks(c, &url)? == pick(&[2, 0]), "after remove")?;

        c.request(
            "playlist_set_tracks",
            json!({ "url": url, "paths": fixtures, "version": removed["version"] }),
        )?
        .ok()?;
        expect(tracks(c, &url)? == pick(&[0, 1, 2]), "after set")
    });

    r.check("playlist_delete (its own playlist)", || {
        if url.is_empty() {
            return Err("nothing was created to delete".into());
        }
        c.request("playlist_delete", json!({ "url": url }))?.ok()?;
        expect(
            fixture_playlist_urls(c)?.is_empty(),
            "the playlist is still listed",
        )
    });
    for leftover in fixture_playlist_urls(c).unwrap_or_default() {
        let _ = c.request("playlist_delete", json!({ "url": leftover }));
    }
}

fn fixture_playlist_urls(c: &mut V6Client) -> Result<Vec<String>, String> {
    let data = c
        .request("playlist_list", json!({ "offset": 0, "limit": 0 }))?
        .ok()?;
    Ok(data["items"]
        .as_array()
        .into_iter()
        .flatten()
        .filter(|p| p["name"] == json!(FIXTURE_PLAYLIST))
        .filter_map(|p| p["url"].as_str().map(String::from))
        .collect())
}

/// Reports the ops a write run leaves alone, rather than running them.
///
/// They write tags or ratings, call last.fm, start or replace playback, or
/// empty the queue, none of which a run can promise to put back.
fn report_unrun_writes(r: &mut Report, has: &impl Fn(&str) -> bool) {
    let destructive: Vec<&str> = [
        "player_next",
        "player_previous",
        "player_stop",
        "player_play",
        "player_pause",
        "now_playing_set_tag",
        "now_playing_set_rating",
        "now_playing_set_lfm",
        "now_playing_list_search",
        "now_playing_list_play",
        "now_playing_list_clear",
        "library_play_all",
        "playlist_play",
        "podcast_episode_play",
    ]
    .into_iter()
    .filter(|op| has(op))
    .collect();
    if !destructive.is_empty() {
        r.skip(
            "writes left alone",
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
    (op, event): (&str, &str),
    cur: Option<&str>,
    a: &'static str,
    b: &'static str,
) {
    if !has(op) {
        return;
    }
    let cur = cur.unwrap_or(a).to_string();
    let other = if cur == a { b } else { a };
    r.check(&format!("{op} + {event} (restore)"), || {
        c.clear_events();
        expect(
            c.request(op, json!({ "mode": other }))?.ok()?["mode"] == json!(other),
            "mode not echoed",
        )?;
        c.wait_event(event)?;
        c.request(op, json!({ "mode": cur }))?;
        c.clear_events();
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
    if ops.iter().any(|o| o == "now_playing_state")
        && let Ok(d) = c.request("now_playing_state", json!({}))?.ok()
        && d["track"].is_object()
    {
        check_track(&d["track"])?;
        seen += 1;
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
    if cond { Ok(()) } else { Err(msg.to_string()) }
}

// ── the V6 client ────────────────────────────────────────────────────────────

pub(crate) struct V6Client {
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
        let (mut client, resp) =
            Self::open(host, port, timeout, &run_client_id("mbrc-conform"), None)?;
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

    /// Connect and handshake as `client_id`, returning the raw response.
    ///
    /// Unlike [`V6Client::connect`] a refusal is not an error here, because the
    /// token checks are about which handshakes the server turns away.
    pub(crate) fn open(
        host: &str,
        port: u16,
        timeout: Duration,
        client_id: &str,
        token: Option<&str>,
    ) -> Result<(Self, v6::IncomingResponse), String> {
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
        let mut data = json!({
            "protocol_version": 6,
            "client_id": client_id,
            "client_type": ClientType::Cli.as_str(),
            "no_broadcast": false,
        });
        if let Some(token) = token {
            data["client_token"] = json!(token);
        }
        client.write_line(&v6::request(0, "handshake", data))?;
        let resp = client.recv(0)?;
        Ok((client, resp))
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
    pub(crate) fn request(
        &mut self,
        op: &str,
        data: Value,
    ) -> Result<v6::IncomingResponse, String> {
        let id = self.send(op, data);
        self.recv(id)
    }

    /// Buffer a non-response frame if it is an event.
    fn buffer_event(&mut self, line: &str) {
        if let Ok(v) = serde_json::from_str::<Value>(line)
            && v.get("kind").and_then(Value::as_str) == Some("event")
        {
            self.events.push(v);
        }
    }

    /// Drop buffered events (call before triggering a change, so a stale event
    /// can't be mistaken for the one under test).
    pub(crate) fn clear_events(&mut self) {
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
            if let Ok(v) = serde_json::from_str::<Value>(line)
                && v.get("kind").and_then(Value::as_str) == Some("event")
            {
                if v["event"] == json!(name) {
                    return Ok(v);
                }
                self.events.push(v);
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
pub(crate) trait RespExt {
    fn ok(&self) -> Result<Value, String>;
    fn err_code(&self) -> Result<String, String>;
    fn err_field(&self) -> Result<Option<String>, String>;
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
    fn err_field(&self) -> Result<Option<String>, String> {
        match &self.result {
            Err(e) => Ok(e.field.clone()),
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

#[cfg(test)]
mod tests {
    use super::*;

    /// The ops that emptied a test MusicBee's queue: a default run must never
    /// send them.
    #[test]
    fn the_sweep_skips_every_op_that_changes_state() {
        for op in [
            "library_queue",
            "now_playing_list_clear",
            "playlist_delete",
            "player_next",
            "now_playing_set_tag",
        ] {
            assert!(!safe_to_sweep(op), "{op} would be sent");
        }
        assert!(safe_to_sweep("library_tracks"));
        assert!(safe_to_sweep("library_changes"));
    }

    #[test]
    fn an_op_this_build_does_not_know_is_not_swept() {
        assert!(!safe_to_sweep("library_reformat_disk"));
        assert!(!safe_to_sweep("handshake"));
    }
}
