# mbrc-tee-proxy

Transparent TCP tee proxy for capturing traces of the MBRC legacy JSON protocol.

Point the Android app at this proxy; every CRLF-terminated JSON frame in
either direction is appended to a JSONL log while bytes are forwarded
unchanged to the real plugin.

The captured traces are the input to the golden-trace tests under
`mbrc-core/tests/golden/` — a replay harness drives them against the
Rust core and byte-diffs responses, which is how we verify legacy
parity before deleting the C# networking stack.

## Build & run

```powershell
cargo run --release --manifest-path tools/mbrc-tee-proxy/Cargo.toml -- `
  --listen 0.0.0.0:3100 `
  --upstream 127.0.0.1:3000 `
  --output tests/golden/session-$(Get-Date -Format yyyyMMdd-HHmmss).jsonl
```

Defaults:

- `--listen 0.0.0.0:3100` — reachable from the LAN so a phone can connect
- `--upstream 127.0.0.1:3000` — the real MusicBee plugin port (adjust to match your `ListeningPort` setting)

## Capture a session

1. Start MusicBee with the current C# plugin (the one we're replacing) running on port `3000` (or whatever you've set in its settings).
2. Start this proxy pointing `--upstream` at that port.
3. On the Android app, change the server address to the machine's LAN IP with port `3100`.
4. Exercise the commands you care about (play/pause, browse library, search, output switch, etc.).
5. Ctrl-C the proxy. The `.jsonl` file is ready to commit.

## Output format

One JSON record per line:

```json
{
  "seq": 0,
  "ts": "2026-04-22T10:04:13.124Z",
  "dir": "c2s",
  "frame": {"context": "player", "data": "Android"},
  "elapsed_ms": 12
}
```

- `seq` — global monotonic counter across both directions
- `dir` — `"c2s"` (client → server) or `"s2c"` (server → client)
- `frame` — parsed JSON object if the frame was valid JSON
- `raw` — present instead of `frame` if the frame wasn't parseable JSON (safety net for partial reads; the legacy protocol is strict JSON so this is rare)
- `elapsed_ms` — ms since the proxy started, useful for eyeballing response latency

## Commit discipline

- Before committing, scrub anything private from the library paths if the trace walked a real user's music collection.
- Name files after the scenario: `tests/golden/library-search-artist-android-v4.jsonl`, `tests/golden/now-playing-list-pagination.jsonl`, etc.
- Keep them small — one scenario per file; the replay harness picks whichever files are relevant.
