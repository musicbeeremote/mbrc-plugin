# MusicBee Remote Protocol - V6

V6 is the **clean-slate** MusicBee Remote protocol (MBRCIP-0003 /
[#118](https://github.com/musicbeeremote/mbrc-plugin/issues/118)). It runs in parallel with
the frozen legacy [V4/V5 protocol](protocol-v4.md) on the same TCP port (default 3000); the
server routes each connection by the shape of its first frame.

Unlike V4/V5 (whose quirks are preserved byte-for-byte and never changed), V6 is under active
development and is what new client work should target.

- **Status:** active development. The op catalog below is the current surface; it grows
  additively and advertises itself via handshake capabilities.
- **Design goals:** a strict, uniform envelope; string enums (not magic ints); typed numeric
  fields; correlation ids; out-of-order responses; best-effort events; capability negotiation.

## Framing

Newline-delimited (`\n`) JSON, one complete JSON object per line. (V4/V5 use CRLF; that is how
the server tells the two apart alongside the first-frame key.) A frame is never split across
lines and never contains a raw newline inside the JSON.

## Envelope

Every frame is a JSON object with a `kind`:

| `kind` | Direction | Shape |
|--------|-----------|-------|
| `request` | client -> server | `{"id":N,"kind":"request","op":"<op>","data":{...}}` |
| `response` | server -> client | `{"id":N,"kind":"response","data":{...}}` **or** `{"id":N,"kind":"response","error":{"code":"..","message":".."}}` |
| `event` | server -> client | `{"kind":"event","event":"<name>","data":{...}}` (no `id`) |

Rules:

- **`id`** is a client-chosen correlation id echoed on the matching response. It is *not* a
  sequence number: responses may arrive out of order, and the client correlates by `id`. The
  handshake is always `id:0`.
- A response carries **exactly one** of `data` or `error`.
- **Unknown additive `data` keys are ignored** (forward-compatible); a structurally invalid
  frame (bad `kind`, missing `op`, not an object) gets a typed error.
- Events have **no `id`** and are best-effort broadcasts to subscribed connections.

## Handshake

The first frame must be the handshake, `id:0`:

```json
{"id":0,"kind":"request","op":"handshake","data":{"protocol_version":6,"client_id":"<uuid>","client_type":"android","no_broadcast":false}}
```

| Field | Required | Notes |
|-------|----------|-------|
| `protocol_version` | yes | must be exactly `6` |
| `client_id` | yes | non-empty string; a **per-install UUID**, stable across relaunches |
| `client_type` | yes | one of `android`, `ios`, `desktop`, `web`, `cli` |
| `no_broadcast` | no | `true` = a command-only / auxiliary socket that receives no events (default `false`) |

Success replies with the server version and its capability surface:

```json
{"id":0,"kind":"response","data":{"server_version":6,"capabilities":{"ops":["handshake","ping",...],"events":["play_state_changed",...]}}}
```

The client should use `capabilities.ops` / `capabilities.events` to degrade gracefully rather
than assume an op exists. A validation failure replies with a typed error (echoing `id:0`) and
closes the connection. A second handshake on an established connection is a protocol error
(`not_allowed`); any non-handshake op *before* the handshake is `unauthorized` + close.

## Error codes

Errors are `{"code":"<code>","message":"<human text>"}`. The `code` is a stable string enum;
the `message` is informational and may change.

| Code | Meaning |
|------|---------|
| `malformed_frame` | not a JSON object / not a valid envelope |
| `unsupported_version` | handshake `protocol_version` is not 6 |
| `missing_field` | a required `data` field is absent |
| `invalid_field` | a field has the wrong type or an unaccepted value |
| `unknown_op` | no such op |
| `unauthorized` | op sent before the handshake |
| `not_allowed` | op not permitted in the current state (e.g. a repeat handshake) |
| `not_found` | the requested resource does not exist (e.g. an unknown cover hash) |
| `unavailable` | a precondition is unmet (e.g. scrobbling with no last.fm account) |
| `internal_error` | an unexpected host/plugin failure |

## Enumerations

All enums are lowercase strings:

- **play_state**: `playing` \| `paused` \| `stopped`
- **shuffle**: `off` \| `shuffle` \| `autodj`
- **repeat**: `none` \| `all` \| `one`
- **lfm_status**: `normal` \| `love` \| `ban`

## Canonical track

Track objects are uniform across every domain (`track_get`, `now_playing_state`,
`library_tracks`, `now_playing_list`). Base fields are always present; the four typed fields
are `null` when unknown; `cover_hash` is omitted when the album has no cached cover.

```json
{
  "src": "C:\\Music\\s.mp3",
  "artist": "Artist", "title": "Title", "album": "Album", "album_artist": "AlbumArtist",
  "track_no": 1, "disc_no": 1, "genre": "Rock",
  "year": 2007,            // int | null (4-digit year parsed from the raw tag)
  "duration_ms": 240000,   // int | null (parsed from "m:ss" / "h:mm:ss")
  "rating": 4.5,           // float | null (0-5)
  "date_added": "2024-01-02T03:04:05Z",  // ISO-8601 UTC | null
  "cover_hash": "<sha1>"   // present only when a cached album cover exists
}
```

`cover_hash` is an album-level content hash; fetch the image with `cover_get`.

## Pagination

Browse/list ops take `{offset?, limit?}` (both default sensibly; `limit:0` means "to the end")
and return:

```json
{"total": 1444, "offset": 0, "items": [ ... ]}
```

`total` is the full count; `items.length` conveys the served window.
