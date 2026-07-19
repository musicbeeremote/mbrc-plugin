# MusicBee Remote Protocol

MusicBee Remote's plugin speaks two wire protocols on the **same TCP port** (default 3000).
The server routes each connection by the shape of its first frame, so a V4/V5 client and a
V6 client can be connected simultaneously.

| Protocol | Status | Framing | Message shape | Reference |
|----------|--------|---------|---------------|-----------|
| **V4 / V5** | Frozen legacy - preserved byte-for-byte, not extended | CRLF (`\r\n`) | `{"context":"<cmd>","data":<...>}` | [protocol-v4.md](protocol-v4.md) |
| **V6** | Clean-slate, active development (MBRCIP-0003 / [#118](https://github.com/musicbeeremote/mbrc-plugin/issues/118)) | newline (`\n`) | envelope `{"id":N,"kind":"request","op":"<op>","data":<...>}` | [protocol-v6.md](protocol-v6.md) |

## Which one am I looking at?

- Shipping **Android (1.1.0-1.6.1) and iOS** clients speak **V4** (iOS negotiates **V5**, a
  thin V4 alias that adds only `nowplayingcurrentposition`). All existing clients keep
  working unchanged - V4/V5 wire frames are byte-identical to what the original C# plugin sent.
- **V6** is the future clean-slate protocol: a strict JSON envelope, newline framing, string
  enums, typed numeric fields, correlation ids, and capability negotiation. It is what new
  client work should target.

## Coexistence

Both protocols are served from one accept loop. Routing is by first-frame shape:

- a first frame with a `context` key -> **legacy** (V4/V5) session;
- a first frame with a `kind` key (an `op:"handshake"` envelope) -> **V6** session;
- anything else -> the connection is closed.

Pre-V4 protocols (V2 / V2.1 / V3) are rejected at handshake and are not documented.
