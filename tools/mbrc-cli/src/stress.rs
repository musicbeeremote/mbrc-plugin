//! `mbrc stress` - concurrent V6 reads against a live server, checked for races.
//!
//! N connections each loop over a fixed mix of read ops for a set time. Half the
//! mix reaches one of MusicBee's process-global query cursors (the now playing
//! list, playlists, an artist's albums, a genre's artists, radio), which the
//! plugin must run one at a time; the rest never touches a cursor and is free to
//! run alongside. Every cursor reply is compared with the one a single client got
//! before the run: two cursor walks interleaving show up as a reply that differs.
//!
//! Read-only. The comparison assumes the queue, playlists and library hold still
//! while it runs, so the baseline is taken again at the end, and a run whose
//! baseline moved reports its mismatches as inconclusive instead of failing.

use std::collections::BTreeMap;
use std::process::ExitCode;
use std::sync::{Mutex, PoisonError};
use std::time::{Duration, Instant};

use serde_json::{Value, json};

use crate::args::{flag_value, run_client_id};
use crate::conform::{RespExt, V6Client};
use crate::monitor::parse_duration;

/// One op in the mix, and whether its reply is held to the baseline.
#[derive(Clone)]
struct Probe {
    op: &'static str,
    data: Value,
    runs_cursor: bool,
}

#[derive(Default)]
struct OpStats {
    latencies_us: Vec<u64>,
    errors: usize,
    mismatches: usize,
    first_error: Option<String>,
}

pub fn run(args: &[String]) -> ExitCode {
    let host = flag_value(args, "--host").unwrap_or_else(|| "127.0.0.1".to_string());
    let Some(port) = number(args, "--port", 3000) else {
        return ExitCode::from(2);
    };
    let Some(connections) = number(args, "--connections", 8) else {
        return ExitCode::from(2);
    };
    let duration = match parse_duration(flag_value(args, "--duration").as_deref().unwrap_or("30s"))
    {
        Some(Some(d)) => d,
        _ => {
            eprintln!("--duration must be a number with optional s/m/h suffix");
            return ExitCode::from(2);
        }
    };
    let timeout = Duration::from_secs(30);

    let mut setup = match connect(&host, port as u16, timeout, "mbrc-stress") {
        Ok(c) => c,
        Err(e) => {
            eprintln!("connect {host}:{port} failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    let mix = match build_mix(&mut setup) {
        Ok(m) => m,
        Err(e) => {
            eprintln!("setup failed: {e}");
            return ExitCode::FAILURE;
        }
    };
    let before = match baseline(&mut setup, &mix) {
        Ok(b) => b,
        Err(e) => {
            eprintln!("baseline failed: {e}");
            return ExitCode::FAILURE;
        }
    };

    println!(
        "stress {host}:{port} - {connections} connections for {}s\n",
        duration.as_secs()
    );
    for probe in &mix {
        println!("  {:<22} {}", probe.op, probe.data);
    }
    println!();

    let stats: Mutex<BTreeMap<&'static str, OpStats>> = Mutex::new(BTreeMap::new());
    let failed_connections = Mutex::new(Vec::new());
    let deadline = Instant::now() + duration;
    std::thread::scope(|s| {
        for worker in 0..connections as usize {
            let (host, mix, before, stats, failed) =
                (&host, &mix, &before, &stats, &failed_connections);
            s.spawn(move || {
                let tag = format!("mbrc-stress-{worker}");
                let mut client = match connect(host, port as u16, timeout, &tag) {
                    Ok(c) => c,
                    Err(e) => {
                        failed
                            .lock()
                            .unwrap_or_else(PoisonError::into_inner)
                            .push(format!("{tag}: {e}"));
                        return;
                    }
                };
                drive(&mut client, worker, mix, before, deadline, stats);
            });
        }
    });

    // The setup connection sat idle through the run, long enough to be dropped.
    drop(setup);
    let closing = connect(&host, port as u16, timeout, "mbrc-stress")
        .and_then(|mut c| baseline(&mut c, &mix));
    let moved = match closing {
        Ok(after) => after != before,
        Err(e) => {
            eprintln!("closing baseline failed: {e}");
            true
        }
    };
    report(
        &stats.into_inner().unwrap_or_else(PoisonError::into_inner),
        &failed_connections
            .into_inner()
            .unwrap_or_else(PoisonError::into_inner),
        moved,
    )
}

/// One connection's loop: the mix in turn, from its own place in it, until the deadline.
fn drive(
    client: &mut V6Client,
    worker: usize,
    mix: &[Probe],
    before: &BTreeMap<&'static str, Value>,
    deadline: Instant,
    stats: &Mutex<BTreeMap<&'static str, OpStats>>,
) {
    let mut turn = worker;
    while Instant::now() < deadline {
        let probe = &mix[turn % mix.len()];
        turn += 1;
        let started = Instant::now();
        let reply = client
            .request(probe.op, probe.data.clone())
            .and_then(|r| r.ok());
        let took = started.elapsed().as_micros() as u64;
        client.clear_events();
        let mut stats = stats.lock().unwrap_or_else(PoisonError::into_inner);
        let entry = stats.entry(probe.op).or_default();
        entry.latencies_us.push(took);
        match reply {
            Ok(data) if probe.runs_cursor && before.get(probe.op) != Some(&data) => {
                entry.mismatches += 1;
            }
            Ok(_) => {}
            Err(e) => {
                entry.errors += 1;
                entry.first_error.get_or_insert(e);
            }
        }
    }
}

fn number(args: &[String], flag: &str, default: u64) -> Option<u64> {
    match flag_value(args, flag).map(|v| v.parse::<u64>()) {
        None => Some(default),
        Some(Ok(n)) if n > 0 => Some(n),
        Some(_) => {
            eprintln!("{flag} must be a positive number");
            None
        }
    }
}

fn connect(host: &str, port: u16, timeout: Duration, tag: &str) -> Result<V6Client, String> {
    let (client, resp) = V6Client::open(host, port, timeout, &run_client_id(tag), None)?;
    resp.ok().map_err(|e| format!("handshake rejected: {e}"))?;
    Ok(client)
}

/// The mix, with a genre and an artist the library really has.
fn build_mix(c: &mut V6Client) -> Result<Vec<Probe>, String> {
    let genre = pick(c, "library_genres", "genre")?;
    let artist = pick(c, "library_artists", "artist")?;
    let cursor = |op, data| Probe {
        op,
        data,
        runs_cursor: true,
    };
    let free = |op| Probe {
        op,
        data: json!({}),
        runs_cursor: false,
    };
    Ok(vec![
        cursor("now_playing_list", json!({ "offset": 0, "limit": 100 })),
        free("player_status"),
        cursor("playlist_list", json!({ "offset": 0, "limit": 100 })),
        free("now_playing_state"),
        cursor("library_artists", json!({ "genre": genre, "limit": 100 })),
        free("now_playing_details"),
        cursor("library_albums", json!({ "artist": artist, "limit": 100 })),
        free("player_output"),
        cursor("library_radio", json!({ "offset": 0, "limit": 50 })),
    ])
}

/// The first name in `op` with between 2 and 500 tracks, so the walk it starts
/// is neither trivial nor the whole library.
fn pick(c: &mut V6Client, op: &str, key: &str) -> Result<String, String> {
    let page = c.request(op, json!({ "offset": 0, "limit": 200 }))?.ok()?;
    let items = page["items"].as_array().cloned().unwrap_or_default();
    let named = |it: &Value| it[key].as_str().filter(|n| !n.is_empty()).map(String::from);
    items
        .iter()
        .find(|it| (2..=500).contains(&it["count"].as_i64().unwrap_or(0)))
        .and_then(named)
        .or_else(|| items.iter().find_map(named))
        .ok_or_else(|| format!("{op} returned no {key}"))
}

fn baseline(c: &mut V6Client, mix: &[Probe]) -> Result<BTreeMap<&'static str, Value>, String> {
    mix.iter()
        .filter(|p| p.runs_cursor)
        .map(|p| Ok((p.op, c.request(p.op, p.data.clone())?.ok()?)))
        .collect()
}

fn report(stats: &BTreeMap<&'static str, OpStats>, failed: &[String], moved: bool) -> ExitCode {
    println!(
        "{:<22} {:>7} {:>5} {:>8} {:>9} {:>9} {:>9}",
        "op", "n", "err", "mismatch", "p50 ms", "p95 ms", "max ms"
    );
    let (mut errors, mut mismatches, mut calls) = (0, 0, 0);
    for (op, s) in stats {
        let mut l = s.latencies_us.clone();
        l.sort_unstable();
        let at = |q: f64| {
            l.get(((l.len() as f64 - 1.0) * q).round() as usize)
                .copied()
        };
        let ms = |v: Option<u64>| v.map_or("-".into(), |us| format!("{:.1}", us as f64 / 1000.0));
        println!(
            "{op:<22} {:>7} {:>5} {:>8} {:>9} {:>9} {:>9}",
            l.len(),
            s.errors,
            s.mismatches,
            ms(at(0.5)),
            ms(at(0.95)),
            ms(l.last().copied())
        );
        if let Some(e) = &s.first_error {
            println!("{:<22} first error: {e}", "");
        }
        errors += s.errors;
        mismatches += s.mismatches;
        calls += l.len();
    }
    println!("\n{calls} calls, {errors} errors, {mismatches} mismatches");
    for f in failed {
        println!("connection failed: {f}");
    }
    if moved {
        println!("the baseline moved during the run, so mismatches are inconclusive");
    }
    if errors > 0 || !failed.is_empty() || (mismatches > 0 && !moved) {
        ExitCode::FAILURE
    } else {
        ExitCode::SUCCESS
    }
}
