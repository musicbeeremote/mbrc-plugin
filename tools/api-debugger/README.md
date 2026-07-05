# MBRC API Debugger

A desktop tool for exercising and debugging the MusicBee Remote wire protocol.
Built with **Tauri 2 + Vue 3 + TypeScript + Tailwind v4** (Rust backend, Vue
frontend). It replaces the former C# / Avalonia debugger.

The wire protocol is newline-delimited JSON (`{"context":"...","data":...}`)
over TCP, default port `3000`.

## Features

- **Direct** - connect straight to a running plugin and send commands. Includes
  a categorized command catalog, an optional secondary connection (mimics the
  Android data-fetch socket with `no_broadcast`), and a live Now-Playing player
  pane.
- **Proxy** - sit between a client and the plugin, forwarding byte-for-byte
  while capturing every frame. Optionally writes a `mbrc-capture/2` golden JSONL
  trace to disk for the replay harness.
- **Sessions** - save the live Direct/Proxy buffers or browse previously
  captured traces, with a virtualized viewer and JSON syntax highlighting.
- **Compare** - schema-diff two sessions (grouped by direction + context
  endpoint) to spot request/response shape drift across plugin versions.
- **Discovery** - find plugin instances on the LAN via UDP multicast
  (`239.1.5.10:45345`), enumerated per network interface.

## Prerequisites

- [Node.js](https://nodejs.org) (see `.nvmrc` for the pinned version)
- [pnpm](https://pnpm.io) (pinned via `package.json` `packageManager`) - use
  `pnpm`, not `npm`
- [Rust](https://rustup.rs) toolchain + the
  [Tauri system dependencies](https://v2.tauri.app/start/prerequisites/) for
  your OS

## Getting started

```bash
cd tools/api-debugger
pnpm install

# run the app (Vite dev server + Tauri window)
pnpm tauri dev
```

Running the Direct panel against a live target needs MusicBee with the MBRC
plugin running.

## Scripts

| Command            | What it does                                    |
| ------------------ | ----------------------------------------------- |
| `pnpm tauri dev`   | Run the app in development (hot reload)         |
| `pnpm build`       | Type-check (`vue-tsc`) + build the frontend     |
| `pnpm tauri build` | Produce a distributable desktop bundle          |
| `pnpm test`        | Run the frontend unit tests (Vitest)            |
| `pnpm test:watch`  | Run Vitest in watch mode                         |

Rust backend tests: `cd src-tauri && cargo test`.

## Layout

```
src/                 Vue frontend
  components/         panels: Direct, Proxy, Sessions, Compare, PlayerPane
  stores/             Pinia stores (own all state + Tauri event subscriptions)
  lib/                pure logic: diff, serialize, commands, player, highlight
src-tauri/src/        Rust backend
  connection.rs        direct socket client
  proxy.rs             tee proxy + mbrc-capture/2 trace writer
  sessions.rs          save / load / import capture files
  discovery.rs         UDP multicast plugin discovery
```

## CI

`.github/workflows/api-debugger.yml` runs on changes under
`tools/api-debugger/**`: frozen-lockfile install, `pnpm build`, Vitest,
`cargo fmt --check`, `clippy -D warnings`, and `cargo test`.