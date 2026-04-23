# mbrc-fixture-trim

Turns raw tee-proxy captures in `tests/captures/` into small, committable
golden fixtures under `mbrc-core/tests/golden/`.

## What it does

1. Reads every `*.jsonl` in `tests/captures/`.
2. Segments each capture into TCP connections by noticing `player` c2s
   frames (every connection starts with one).
3. Classifies each connection as `(platform, protocol_version)` from the
   handshake — `android`, `ios`, or `apidebugger` when no platform tag
   was sent; protocol 2, 3, or 4.
4. Rewrites long base64 cover payloads and lyric strings to tiny
   deterministic placeholders. **Envelope shapes are preserved exactly**
   — status codes, error branches, the V2 raw-string vs V3+ object form
   all pass through unchanged. Only the bulk bytes are swapped.
5. Dedups byte-identical frames (after substitution, ignoring seq/ts)
   within each bucket so the Android client's 200 reconnects don't turn
   into 200 copies of the same handshake.
6. Writes one JSONL per `(platform, protocol_version)` bucket to
   `mbrc-core/tests/golden/legacy-v{N}-{platform}.jsonl`.
7. Extracts the placeholder PNG to
   `mbrc-core/tests/golden/_assets/placeholder-cover.png` so the replay
   harness can seed its mock `MbrcCallbacks` with the same bytes and get
   byte-exact diffs.

## Run

```powershell
cargo run --release --manifest-path tools/mbrc-fixture-trim/Cargo.toml
```

No args — it hardcodes `tests/captures/` → `mbrc-core/tests/golden/`
because this is a one-shot fixture generator, not a pipeline.

## When to re-run

- Whenever you capture a new scenario with the tee-proxy and want to
  fold it into the committed fixtures. Drop the new JSONL into
  `tests/captures/` and rerun.
- The input directory is `.gitignore`'d; the output directory is
  checked in.
