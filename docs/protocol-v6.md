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

## Discovery

Both discovery channels are shared with the legacy protocol and documented in full under
[protocol-v4.md](protocol-v4.md#discovery). What matters for a V6 client is how to tell, before
connecting, whether a server speaks V6:

| channel | how to ask | answer |
|---|---|---|
| mDNS / DNS-SD | browse `_mbrc._tcp.local.` | TXT `protocol` includes `6` |
| UDP multicast (`239.1.5.10:45345`) | send `{"address":"<your ip>","protocol":true}` | reply carries `"protocol":"4,5,6"` |

Both are advisory, and both answer without opening a TCP connection. A server that predates V6
answers the UDP probe with no `protocol` key at all and advertises `4,5` over mDNS, so its
absence is the negative answer.

**Do not probe by opening a connection.** A pre-V6 server parses a V6 handshake frame as JSON,
finds no `context`, and drops it *silently* while holding the socket open - so a TCP probe
cannot distinguish "does not speak V6" from "slow", and it spends a connection against a
server whose per-IP cap you may be approaching. Ask discovery instead.

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

### `client_id` is an identifier, not a credential

Nothing authenticates a `client_id`. Any peer that can reach the port can present one and the
registry will treat it as that installation, including superseding its main connection. On a
LAN that is the trust model V4 had too, but two consequences are worth stating plainly:

- **Anything permission-bearing must not trust `client_id` alone.** If persistent per-install
  state is ever hung off it (Party Mode, a known-clients list), that needs a real credential.
- **A cloned id is a live hazard, not a theoretical one.** A device backup restored onto a
  second device carries the same persisted UUID, and the two installs then supersede each
  other's main connection in a loop. A client seeing repeated unexplained supersession should
  regenerate its id.

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
| `not_allowed` | op not permitted in the current state (a repeat handshake), or the connection was refused by the per-client cap - sent instead of the handshake acceptance, then the socket closes |
| `not_found` | the requested resource does not exist (e.g. an unknown cover hash) |
| `stale_list` | the now-playing list moved since the `version` the request carried |
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
  "duration_ms": 240000,   // int | null (parsed from "m:ss" / "h:mm:ss")*
  "rating": 4.5,           // float | null (0-5)
  "date_added": "2024-01-02T03:04:05Z",  // ISO-8601 UTC | null
  "cover_hash": "<sha1>"   // present only when a cached album cover exists
}
```

`cover_hash` is an album-level content hash; fetch the image with `cover_get`.

\* `duration_ms` is parsed from MusicBee's formatted tag, the only per-path source there is,
so it is second-granular. The **playing** track is the exception: `now_playing_state` serves
the player's own exact millisecond duration, matching the `duration_ms` beside it.

## Keepalive (normative)

**The client pings; the server listens.** V4 pushed a server ping every
`ping_interval_secs` to every subscriber; V6 inverts it, because the side that knows whether
it still cares is the side that should speak, and it halves the idle traffic.

- A V6 client SHOULD send `{"op":"ping"}` every **15 seconds** while it holds a connection it
  wants kept - an event subscription especially.
- A V6 connection silent for **three intervals** is closed. Any frame counts, not just a ping:
  a client issuing requests is self-evidently alive.
- The ping doubles as a NAT and Wi-Fi power-save keepalive. A phone that stops pinging loses
  its event socket, which is the intended outcome - reconnect and re-query.

Legacy V4/V5 keeps the opposite arrangement (server-pushed ping, subscribers never reaped),
because its shipped clients were built against it.

## Pagination

Browse/list ops take `{offset?, limit?}` and return:

```json
{"total": 1444, "offset": 0, "items": [ ... ]}
```

`total` is the full count; `items.length` conveys the served window. `offset` defaults to 0.

**`limit` defaults to 1000**, so the laziest possible request - `library_tracks {}` - reads a
page rather than every tag in the library. An explicit **`limit: 0` still means "to the end"**;
on a large library that is a deliberate choice, and an expensive one. `now_playing_list` also
returns a `version` - see [Now Playing List](#now-playing-list-the-queue).

## Op catalog

### System

| Op | Request `data` | Response |
|----|----------------|----------|
| `system_info` | `{}` | `{"plugin_version":"<real build version>","protocol_version":6}` (unlike V4's pinned `pluginversion`, this is the actual plugin build) |

### Player

| Op | Request `data` | Response |
|----|----------------|----------|
| `player_play` / `player_pause` / `player_play_pause` / `player_stop` | `{}` | `{}` |
| `player_next` / `player_previous` | `{}` | `{}` |
| `player_status` | `{}` | `{"play_state":"playing","volume":75,"muted":false,"shuffle":"off","repeat":"none","scrobbling":true}` |
| `player_set_volume` | `{"volume":0-100}` | `{"volume":<new>}` |
| `player_set_mute` | `{"muted":bool}` | `{"muted":<new>}` |
| `player_set_shuffle` | `{"mode":"off"\|"shuffle"\|"autodj"}` | `{"mode":<new>}` |
| `player_set_repeat` | `{"mode":"none"\|"all"\|"one"}` | `{"mode":<new>}` |
| `player_set_scrobbling` | `{"enabled":bool}` | `{"enabled":<new>}` (`unavailable` if enabling without a last.fm account) |
| `player_output` | `{}` | `{"active":"Speakers","devices":["Speakers","Headphones"]}` |
| `player_set_output` | `{"device":"<name>"}` | `{"active":<new>,"devices":[...]}` |

> Setters echo the new canonical value in their response. `shuffle`/`repeat` have **no**
> dedicated broadcast event, so the reply is the only state signal for those.

### Track

| Op | Request `data` | Response |
|----|----------------|----------|
| `track_get` | `{"src":"<path>"}` | the [canonical track](#canonical-track) |
| `cover_get` | `{"hash":"<sha1>","client_hash?":"<sha1>"}` | `{"hash":..,"image":"<base64>"}`, or `{"hash":..,"not_modified":true}` when `client_hash` matches, or `not_found` |

### Now Playing

| Op | Request `data` | Response |
|----|----------------|----------|
| `now_playing_state` | `{include_list_order?}` | `{"track":<canonical\|null>,"list_order":<int\|null>,"position_ms":..,"duration_ms":..,"lfm_status":".."}` |
| `now_playing_details` | `{}` | extended tags (publisher/composer/counts/format/bitrate/...) |
| `now_playing_position` | `{}` | `{"position_ms":..,"duration_ms":..}` |
| `now_playing_lyrics` | `{}` | `{"type":"synced"\|"plain"\|"none","lines":[{"text":..,"at_ms?":..}]}` |
| `now_playing_seek` | `{"position_ms":N}` | `{"position_ms":..,"duration_ms":..}` (read back after the seek) |
| `now_playing_set_rating` | `{"rating":0-5\|null}` | `{"rating":<new>}` |
| `now_playing_set_lfm` | `{"status":"normal"\|"love"\|"ban"}` | `{"lfm_status":<new>}` |
| `now_playing_set_tag` | `{"tag":"<name>","value":"<v>"}` | `{}` |

`now_playing_lyrics` returns structured lyrics; synced lines carry `at_ms`, plain lines do not,
and `type:"none"` yields an empty `lines`.

### Now Playing List (the queue)

One canonical list (no per-client-type variants). Each item is the
[canonical track](#canonical-track) plus **three** 0-based indices:

- **`order`** - the absolute MusicBee list index. This IS the key the mutations consume, so
  `now_playing_list_play`/`remove`/`move` take exactly this value. (In the default view it is
  contiguous; in the up-next view it follows shuffle and is non-contiguous.)
- **`position`** - the sequential display rank within the returned window (`offset`, `offset+1`, …).
- **`play_position`** - the rank in the shuffle **play** order (`0` = current, `1` = next up, …), or
  **`-1` if the track has already been played**. Lets the default view show play order + played state.

`now_playing_list` has two views via `up_next`:

- **default** (`up_next` absent/false): the **full list in list order** - every track, played and
  unplayed, as MusicBee holds it. Here `order == position ==` the storage index, and `play_position`
  marks each track's place in the shuffle order (or `-1` = already played).
- **`up_next: true`**: MusicBee's shuffle-aware **play order from the current track**; already-played
  tracks are dropped. `order` is the true storage index; `position == play_position`.

| Op | Request `data` | Response |
|----|----------------|----------|
| `now_playing_list` | `{offset?, limit?, up_next?}` | `{total, offset, version, items}` - canonical tracks + `order` + `position` + `play_position` |
| `now_playing_list_play` | `{"order":N, "version?":V}` | `{}` |
| `now_playing_list_remove` | `{"order":N, "version?":V}` | `{}` |
| `now_playing_list_move` | `{"from":N,"to":M, "version?":V}` | `{}` - `from`/`to` are `order` values |
| `now_playing_list_search` | `{"query":"<text>"}` | `{}` |
| `now_playing_queue` | `{"paths":[..],"mode?":"next"\|"last"\|"now"\|"add_all","play?":"<path>"}` | `{}` |

**`version` and the mutation guard.** MusicBee addresses the queue by index alone, so an
`order` only means what you read while the list it came from still holds. Every page carries
the queue's `version`; pass it back on a mutation and a queue that moved in between is refused
`stale_list` instead of hitting the wrong slot - re-read the page and retry. The field is
optional: send none and the mutation is unguarded, as before. Batch versioned removal is
[#110](https://github.com/musicbeeremote/mbrc-plugin/issues/110).

> `mode` is `snake_case` like every other V6 enum, so the last one is **`add_all`** - V4 spells
> it `add-all` on its own wire, and V6 rejects that spelling. An unrecognized mode is
> `invalid_field`, never silently treated as `next`.

> A single view that is *both* shuffle-play-order *and* keeps already-played tracks is impossible -
> MusicBee's `GetNextIndex` is forward-only, so a played track's play order can't be recovered. The
> `play_position: -1` marker is the answer instead (#118 §7 / #94).

### Library

| Op | Request `data` | Response |
|----|----------------|----------|
| `library_genres` | `{offset?, limit?}` | page of `{"genre":..,"count":..}` |
| `library_artists` | `{offset?, limit?, album_artists?, genre?}` | page of `{"artist":..,"count":..}` |
| `library_albums` | `{offset?, limit?, artist?}` | page of `{"album":..,"artist":..,"count":..}` (+ `cover_hash` when cached) |
| `library_tracks` | `{offset?, limit?, album?}` | page of [canonical tracks](#canonical-track) |
| `library_radio` | `{offset?, limit?}` | page of `{"name":..,"url":..}` |
| `library_play_all` | `{"shuffle?":bool}` | `{}` |

### Playlist

| Op | Request `data` | Response |
|----|----------------|----------|
| `playlist_list` | `{offset?, limit?}` | page of `{"url":..,"name":..}` |
| `playlist_play` | `{"url":"<path>"}` | `{}` |

## Events

Broadcast to every subscribed (non-`no_broadcast`) connection, best effort. Most are marker
events - they carry `{}` (or a small hint like `cover_cache_changed`'s `building`) and mean
"re-query"; the client refetches the relevant op rather than trusting the event payload as state.

| Event | `data` | Fires when |
|-------|--------|-----------|
| `play_state_changed` | `{"play_state":".."}` | playback starts/pauses/stops |
| `volume_changed` | `{"volume":N}` | volume changes |
| `mute_changed` | `{"muted":bool}` | mute toggles |
| `now_playing_changed` | `{"artist":..,"title":..,"album":..,"path":..}` | the track changes |
| `now_playing_lyrics_changed` | `{}` | lyrics finished loading for the current track -> re-query `now_playing_lyrics` |
| `now_playing_list_changed` | `{}` | the queue changed -> re-query `now_playing_list` |
| `cover_cache_changed` | `{"building":bool}` | album-cover cache changed (`building` = a build is in progress vs finished) -> re-resolve `cover_hash` |
| `library_changed` | `{}` | the library changed (add/scan/switch) -> re-browse |
| `server_shutdown` | `{}` | the server is going away deliberately (MusicBee closing, networking stopped) |

**Clients MUST ignore events they do not recognize.** The catalog grows additively, so an
unknown `event` name is skipped, never treated as an error. Without this rule no event can
ever be added without breaking a shipped client.

`server_shutdown` is what separates a deliberate stop from a network drop: on receiving it a
client should stop reconnecting until it discovers the server again, rather than retrying into
a MusicBee that is closing. It is best-effort like every other event - a crash or a pulled
cable produces no goodbye, so a dropped connection with no preceding `server_shutdown` still
means "retry".

> There is intentionally no `shuffle_changed` / `repeat_changed` / `lfm_changed` event yet, so
> those states only refresh on the next state fetch (or from a setter's reply). Candidate
> additions, tracked against #118.

## Differences from V4 / V5

| | V4 / V5 | V6 |
|--|---------|-----|
| Framing | CRLF | newline |
| Message | `{context, data}` | envelope `{id, kind, op/event, data/error}` |
| Enums | magic ints / strings, mixed | lowercase string enums |
| Numbers | often stringified (`"81"`) | typed (`81`, `4.5`, `null`) |
| Correlation | positional / implicit | explicit `id`, out-of-order allowed |
| Dual sockets | required pattern (broadcast + command) | optional (`no_broadcast`); one socket suffices |
| Discovery of surface | hardcoded per version | handshake `capabilities` |
| Now-playing list | Android sequential vs iOS ordered variants (1-based, quirks) | one canonical list + `up_next` view; typed `order` (mutation key) / `position` / `play_position` |

## Notes for tooling

- CLI: `mbrc send --protocol 6 --op <op> --json '<data>'` drives one op; it stays a broadcast
  subscriber during `--wait-ms` so events print. `mbrc conform` validates the surface;
  `mbrc fuzz --protocol 6` stress-tests robustness (read-only).
- The committed wire snapshots under `packages/mbrc-core/tests/golden/v6/` are the byte-exact
  reference for every response shape here (regenerate with `MBRC_BLESS=1`).
