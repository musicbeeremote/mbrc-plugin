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

## Transports

V6 speaks over four transports on the **same port** (default 3000). The op catalog,
the envelope and the error codes are identical on all of them: only the framing and
the way a subscription is expressed differ.

The server tells them apart by peeking at the first four bytes of a connection. A
frame that begins `GET `, `POST`, `HEAD`, `PUT `, `DELE`, `OPTI` or `PATC` is HTTP;
anything else is parsed as a JSON first frame and routed to V4/V5 or V6 by its shape.
The peek happens inside the un-handshaked window, and its clock starts before the
sniff, so a client that connects and says nothing is reaped exactly as it would have
been without it.

### 1. TCP socket (the native transport)

Connect to the port, send the [handshake](#handshake), then send request frames and
read response and event frames as [newline-delimited JSON](#framing). This is the
only transport that holds session state, so `handshake` and `ping` exist here alone.
Events arrive unsolicited on the same socket unless the handshake asked for
`no_broadcast`.

### 2. HTTP-RPC - `POST /api/v6/{op}`

The request body is the op's `data`, and the response body is the response `data`.
No envelope: there is no `id` to correlate because the answer is the response to
this request, and no `kind` because HTTP already says which is which.

```
POST /api/v6/library_tracks
Content-Type: application/json

{"album": "Panic", "limit": 2}

200 OK
{"total": 14, "offset": 0, "items": [ ... ]}
```

A failure carries the same V6 error object, with a status chosen to match its code so
a browser, a proxy and `curl` all read it correctly:

| Error code | Status |
|---|---|
| `malformed_frame`, `missing_field`, `invalid_field`, `unsupported_version` | 400 |
| `unauthorized`, `invalid_token` | 401 |
| `not_allowed`, `forbidden` | 403 |
| `unknown_op`, `not_found` | 404 |
| `stale_list` | 409 |
| `unavailable` | 503 |
| `internal` | 500 |

**`handshake`, `ping` and `pair` are not offered here.** The first two are meaningless
without a connection to hold their state, and a browser pairs through `POST /api/pair`;
asking for any of them is `unknown_op`.

`GET /api/v6/capabilities` returns the same capability object the handshake carries,
plus this caller's [`permissions`](#what-a-client-is-told), so an HTTP-only client can
discover the surface without a socket.

### 3. WebSocket - `GET /ws`

An upgrade on the same port. Once open it is the socket transport exactly: send the
handshake, then exchange newline-delimited JSON frames, one JSON object per message.
This is the transport a browser uses when it can hold a connection, and the only one
besides TCP that carries events without polling.

The pairing token travels in the **query string** (`/ws?token=...`) rather than a
header, because a browser's `WebSocket` constructor cannot set one.

### 4. Server-sent events - `GET /api/events`

The broadcast half of the protocol for a client that cannot hold a socket. Each
message carries one event frame, in the same envelope the socket uses:

```
: open

data: {"kind":"event","event":"play_state_changed","data":{"play_state":"playing"}}

data: {"kind":"event","event":"volume_changed","data":{"volume":46}}
```

It needs no handshake: the stream is one-way, opening it is the whole subscription
and closing it is the whole goodbye. The opening `: open` comment exists because the
response head is not flushed until the body produces something, and a player that
changes nothing for an hour would otherwise look like a stream that never opened.

Paired with HTTP-RPC for commands, SSE gives a complete client without a socket. The
token travels in the query string here too, for the same reason: `EventSource` takes
a URL and nothing else.

## Pairing and authentication

Pairing is off by default (`web_auth_required`), and the admission check is the same
function whether it is on or off, so the guarded path is never an untested branch.

**Ask first.** `GET /api/pair/status` answers `{"auth_required":bool,"paired":bool}`.
`paired` is about *this* caller, not the server: the token lives in a cookie the page
cannot read, so asking is the only way a browser can know whether it already has one.
A client should skip its pairing screen entirely when `auth_required` is false.

**Redeem a code.** The user reads a six-digit code out of MusicBee's Configure panel;
it lasts two minutes, and grants the [Party Mode role](#party-mode) it was made for.
`POST /api/pair` with `{"code":"123456","label":"Firefox"}` answers
`{"token":"...","role":"host"}` and sets the token as a cookie:

```
Set-Cookie: mbrc_token=<token>; HttpOnly; SameSite=Strict; Path=/; Max-Age=31536000
```

A wrong or expired code is 401. A spent code is refused: redeeming is one-shot. Pairing
works whether or not `web_auth_required` is on: with it off, a browser pairs to gain a
Party Mode role rather than to get in at all.

#### Wrong codes

A code that grants Host is worth guessing, so wrong codes are limited, and browser pairing
and the V6 [`pair`](#pair) op share the count:

- **Per address:** three wrong codes are free. After that the address must wait 5 seconds,
  doubling with each further wrong code up to 5 minutes, and while it waits even the right
  code is refused without being looked at. The right code clears the address's count.
- **Per code:** the tenth wrong code from any mix of addresses voids the live code, and the
  panel says so. That bounds a guess at ten in a million per code.

A refusal while waiting is still 401 / `unauthorized`, with a message saying how long.

**Present it.** A token is accepted three ways, checked in this order:

1. the `mbrc_token` cookie, which a browser sends by itself;
2. `Authorization: Bearer <token>`, for a native client;
3. `?token=<token>` on `/ws` and `/api/events`, where neither of the above is possible.

The cookie is why a browser works at all under pairing. An `<img>`, a `WebSocket` and
an `EventSource` cannot set a header, and covers, the socket and the stream are
exactly the three things a page needs. `HttpOnly` also puts the token beyond any
script on the page, which `localStorage` never did.

Tokens do not expire and are stored **hashed**, so the file cannot be replayed as a
credential. Pairings outlive the process: a phone is paired once, not once per launch.
A browser can be listed, renamed and unpaired individually from the Configure panel.

**What is guarded.** Under `web_auth_required`, `/api/v6/*`, `/api/cover/*`, `/ws` and
`/api/events` all answer 401 to an unpaired caller. `/` and `/api/pair*` stay open, so
the pairing screen can load and pair. An unpaired caller always sees the same shape:

```json
{"error": {"code": "unauthorized", "message": "pair this browser first"}}
```

**Two guards apply whether pairing is on or not**, because an attacker who cannot pair
can still point a name they control at this address:

- a **Host allowlist** - IP literals, `localhost`, a single label (a dotless name
  cannot be delegated in the root, so nobody off this network can aim one here) and
  the suffixes reserved for private use. A router's invented suffix under a real gTLD
  is not admitted: `.box` is delegated, so a `fritz.box` is a name someone else can
  come to hold. A rejected Host is 421.
- a **CSP** admitting only this origin.

## Party Mode

Party Mode lets the person running MusicBee hand the remote to a room full of guests
without handing over the whole player. **With it off, nothing changes**: every client may
do everything, and is told so. It is switched in MusicBee's Configure panel (and persisted
as `party_mode_enabled` in `core_settings.json`); no client op can switch it or change a
role, not even a host's.

### Roles and capabilities

Every client has a role. A client nobody assigned has the default role, **Guest**.

| Role | Capabilities |
|------|--------------|
| `host` | all of them |
| `dj` | `playback`, `queue_add`, `queue_insert`, `queue_replace`, `queue_edit`, `volume`, `modes` |
| `guest` | `queue_add`, limited to **one track per request** |
| `listener` | none |

Reads (status, browsing, lists, covers, lyrics) are allowed in every role.

| Capability | Ops |
|------------|-----|
| `playback` | `player_play`, `player_pause`, `player_play_pause`, `player_stop`, `player_next`, `player_previous`, `now_playing_seek`, `now_playing_list_play`, `now_playing_list_search` |
| `queue_add` | `now_playing_queue`, `library_queue`, `podcast_episode_play` with mode `last` |
| `queue_insert` | the same three with mode `next` |
| `queue_replace` | the same three with mode `now` or `add_all`; `library_play_all`, `playlist_play` |
| `queue_edit` | `now_playing_list_remove`, `now_playing_list_move`, `now_playing_list_clear` |
| `volume` | `player_set_volume`, `player_set_mute` |
| `modes` | `player_set_shuffle`, `player_set_repeat`, `player_set_stop_after_current`, `player_set_scrobbling` |
| `library_edit` | `now_playing_set_rating`, `now_playing_set_lfm`, `now_playing_set_tag` |
| `playlist_edit` | `playlist_create`, `playlist_delete`, `playlist_add_tracks`, `playlist_remove_tracks`, `playlist_move_tracks`, `playlist_set_tracks` |
| `output` | `player_set_output` |

A queueing op is judged by its **effective** mode, defaults included: `now_playing_queue`
defaults to `next`, `library_queue` to `last`, `podcast_episode_play` to `now`. A `mode` the
op would reject counts as `queue_replace`. A guest may queue exactly one track to the end:
`now_playing_queue` with `mode:"last"` and one path, or `podcast_episode_play` with
`mode:"last"`. `library_queue` names a scope whose size only the server knows, so a guest may
not use it at all.

A refused op answers `forbidden` (HTTP 403 on HTTP-RPC) with a message naming the capability
it needed, or the one-track limit:

```json
{"id":7,"kind":"response","error":{"code":"forbidden","message":"`player_next` needs the `playback` permission, which this client does not have"}}
```

### Who is who

A role rests on something a guest cannot copy.

| Client | Known by | Gains a role by |
|--------|----------|-----------------|
| V6 app on a socket | `client_id`, once its `client_token` checked out | the `pair` op |
| Browser (WebSocket, HTTP-RPC, SSE) | its pairing token | `POST /api/pair` |
| Android 1.6 (V4) | its plain-text `client_id` | the Configure panel; weaker trust, since anyone who reads the id off the network can claim it |
| iOS, Android 1.5 and older (V4) | nothing | cannot: always the default role |

A WebSocket is judged by the pairing token it presented when it upgraded, never by the
`client_id` its page makes up, so a browser that has just paired opens a new socket. A V6
identity with a role is never pruned from the server's identity store, and an id that is
issued a fresh token loses any role left on it.

### What a client is told

The handshake reply and `GET /api/v6/capabilities` carry the caller's `permissions`:

```json
"permissions": {"party_mode": true, "role": "guest", "allowed": ["queue_add"], "max_tracks_per_add": 1}
```

With Party Mode off it is `{"party_mode": false, "role": "host", "allowed": [<every capability>]}`.
`max_tracks_per_add` is present only for a role that has the limit. A client should draw
only what `allowed` permits, and treat a capability it does not recognize as gating nothing.

`permissions_changed` carries the same object, sent to a connection that takes events when
its own permissions change: Party Mode switched, or its role changed. A change to another
client's role sends nothing.

### `pair`

A V6 app on a socket gains a role by redeeming a pairing code. The code is made in the
Configure panel with the role it grants (Host preselected) and lasts two minutes.

```json
→ {"id":3,"kind":"request","op":"pair","data":{"code":"123456"}}
← {"id":3,"kind":"response","data":{"role":"dj"}}
```

`pair` is answered whatever the caller's role, since pairing is how a guest stops being
one. A wrong or expired code, or an address that must wait (see
[wrong codes](#wrong-codes)), is `unauthorized`. An app whose `client_id` the server cannot
prove (it runs without a database) gets `unavailable` and the code stays live. On a
browser's WebSocket it is `not_allowed`: a browser pairs through `POST /api/pair`. It is not
offered over HTTP-RPC.

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
| `client_id` | yes | non-empty string, at most 128 chars; a **per-install UUID**, stable across relaunches |
| `client_token` | after the first | the token this server issued for that `client_id` - see below |
| `client_type` | yes | one of `android`, `ios`, `desktop`, `web`, `cli` |
| `no_broadcast` | no | `true` = a command-only / auxiliary socket that receives no events (default `false`) |
| `client_name` | no | what the device is called, for the Configure panel's device list ("Pixel 8"); trimmed and cut at 64 chars; a later handshake's name replaces it |

Success replies with the server version, its capability surface, and what this client may
do under [Party Mode](#what-a-client-is-told):

```json
{"id":0,"kind":"response","data":{"server_version":6,"capabilities":{"ops":["handshake","ping","pair",...],"events":["play_state_changed",...]},"permissions":{"party_mode":false,"role":"host","allowed":[...]}}}
```

The client should use `capabilities.ops` / `capabilities.events` to degrade gracefully rather
than assume an op exists. A validation failure replies with a typed error (echoing `id:0`) and
closes the connection. A second handshake on an established connection is a protocol error
(`not_allowed`); any non-handshake op *before* the handshake is `unauthorized` + close.

### `client_token` - telling two installs with the same id apart

`client_id` is an identifier, not a credential. It exists so the server can tell one
installation from another: supersede that install's stale main connection, count it against
the per-client cap, label it in the log. Nothing is gated on it and nothing should be.

It only has to be *unique*. The case where it stops being unique is a **restored device
backup**: the clone carries the same persisted UUID, both installs claim it, and the two evict
each other's main connection in a loop. `client_token` is the tiebreaker that settles which of
them is the original. The server issues one on first contact and remembers it.

```json
// first handshake for an unseen client_id - no client_token sent
← {"id":0,"kind":"response","data":{"server_version":6,"capabilities":{...},
                                    "client_token":"a3f1…"}}

// every handshake after that
→ {"id":0,"kind":"request","op":"handshake",
   "data":{"protocol_version":6,"client_id":"<uuid>","client_type":"android","client_token":"a3f1…"}}
← {"id":0,"kind":"response","data":{"server_version":6,"capabilities":{...}}}
```

**What the client must do**

1. **Persist `client_token` alongside `client_id`, and persist whatever `client_token` any
   response carries** - not only the first. The server's store is bounded: an entry is dropped
   after 30 days unseen, or evicted once more than 200 are held (least-recently-seen first). A
   returning client whose record has aged out is simply unknown again, and is issued a fresh
   token to keep.
2. **Send it on every handshake once you have one.** A response without `client_token` means
   the server already knew you, which is the normal case.
3. **On `invalid_token`, generate a new `client_id`, discard the token, and handshake again.**
   The id you hold is already claimed by another installation - which, if you are a restored
   backup, is exactly true. A new id is a new installation identity, which is what you are. Do
   not retry the same id: it will be refused every time.

**The token is not authentication on its own.** Anyone who can reach the port can make up a
new `client_id` and be issued a token for it, so the token proves only that you are the
installation that first claimed this id. That is enough to **hold** a [Party Mode](#party-mode)
role once pairing has granted one: nobody else can present your token, and a made-up id only
ever gets the default role. An identity with a role is never pruned from the store.

## Error codes

Errors are `{"code":"<code>","message":"<human text>","field":"<name>"}`. The `code` is a
stable string enum; the `message` is informational and may change. **`field` is present only
when the failure is about one named `data` field** (`missing_field`, `invalid_field` and
friends), so validation can be handled without parsing the message - a client can point at the
offending input directly. Its absence means the error is not about a single field.

| Code | Meaning |
|------|---------|
| `malformed_frame` | not a JSON object / not a valid envelope |
| `unsupported_version` | handshake `protocol_version` is not 6 |
| `missing_field` | a required `data` field is absent |
| `invalid_field` | a field has the wrong type or an unaccepted value |
| `invalid_token` | the `client_id` is held by an installation with a different `client_token` - generate a new `client_id` and retry |
| `unknown_op` | no such op |
| `unauthorized` | op sent before the handshake |
| `not_allowed` | op not permitted in the current state (a repeat handshake), or the connection was refused by the per-client cap - sent instead of the handshake acceptance, then the socket closes |
| `forbidden` | Party Mode is on and this client's role does not allow the op; the message names the capability it needs |
| `not_found` | the requested resource does not exist (e.g. an unknown cover hash) |
| `stale_list` | the queue or playlist moved since the `version` the request carried |
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
`library_tracks`, `now_playing_list`, `playlist_tracks`). Base fields are always present; the
four typed fields are `null` when unknown; `cover_hash` is omitted when the album has no
cached cover.

A list may add index fields beside these - `order` and `position` on a playlist, plus
`play_position` on the queue - but never changes the track's own shape.

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

`cover_hash` is an album-level content hash. Fetch the image with `cover_get` over any
transport, or - because it is content-addressed and so can be cached forever - straight from
`GET /api/cover/{hash}`, which is what lets a browser put one in an `<img src>`.

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
| `pair` | `{"code":"123456"}` | `{"role":"host"}` - see [`pair`](#pair); socket only |

### Player

| Op | Request `data` | Response |
|----|----------------|----------|
| `player_play` / `player_pause` / `player_play_pause` / `player_stop` | `{}` | `{}` |
| `player_next` / `player_previous` | `{}` | `{}` |
| `player_status` | `{}` | `{"play_state":"playing","volume":75,"muted":false,"shuffle":"off","repeat":"none","scrobbling":true,"stop_after_current":false}` |
| `player_set_volume` | `{"volume":0-100}` | `{"volume":<new>}` |
| `player_set_mute` | `{"muted":bool}` | `{"muted":<new>}` |
| `player_set_shuffle` | `{"mode":"off"\|"shuffle"\|"autodj"}` | `{"mode":<new>}` |
| `player_set_repeat` | `{"mode":"none"\|"all"\|"one"}` | `{"mode":<new>}` |
| `player_set_scrobbling` | `{"enabled":bool}` | `{"enabled":<new>}` (`unavailable` if enabling without a last.fm account) |
| `player_set_stop_after_current` | `{"enabled":bool}` | `{"enabled":<new>}` |
| `player_output` | `{}` | `{"active":"Speakers","devices":["Speakers","Headphones"]}` |
| `player_set_output` | `{"device":"<name>"}` | `{"active":<new>,"devices":[...]}` |

> Setters echo the state that was asked for, not a read-back of the player. MusicBee applies
> auto-DJ asynchronously, so reading the player in the same breath as the write describes the
> state *before* it: a `player_set_shuffle` of `shuffle` answered `off`, and the three-way
> cycle collapsed to off/on/off. A setter fails when MusicBee refuses, so one that returned
> `{}` may answer with the mode it was given.
>
> `shuffle_changed`, `repeat_changed` and `scrobbling_changed` are broadcast as well, so a
> client also learns about a change made in MusicBee's own window rather than only about its
> own writes.

> **Stop-after-current is one-shot.** MusicBee clears it the moment it fires, and
> announces nothing when it does, so `stop_after_current_changed` is emitted by the
> same one-second poll that watches shuffle, repeat and scrobbling rather than by the
> notification. A client sees `play_state_changed` to `stopped` and then, within a
> second, `stop_after_current_changed` to `false`. Do not assume the mode survives the
> track it was armed for.

> **Stop-after-current takes a value, never a toggle.** V4 spells it as one; V6 does not,
> because `enabled` would have to be `bool | "toggle"` and a toggle sent against a stale
> reading lands on the opposite of what was asked for. The current value is a field on
> `player_status` and `stop_after_current_changed` announces every change, so a client that
> wants to flip it always knows what it is flipping from. MusicBee offers no setter of its
> own here, only a call that inverts the flag, so asking for the value already held does
> nothing rather than inverting it.

### Track

| Op | Request `data` | Response |
|----|----------------|----------|
| `track_get` | `{"src":"<path>"}` | the [canonical track](#canonical-track) |
| `cover_get` | `{"hash":"<sha1>","client_hash?":"<sha1>"}` | `{"hash":..,"image":"<base64>"}`, or `{"hash":..,"not_modified":true}` when `client_hash` matches, or `not_found` |

### Now Playing

| Op | Request `data` | Response |
|----|----------------|----------|
| `now_playing_state` | `{include_list_order?}` | `{"track":<canonical\|null>,"list_order":<int\|null>,"position_ms":..,"duration_ms":..,"lfm_status":".."}` |
| `now_playing_details` | `{}` | extended tags - see below |
| `now_playing_position` | `{}` | `{"position_ms":..,"duration_ms":..}` |
| `now_playing_lyrics` | `{}` | `{"type":"synced"\|"plain"\|"none","lines":[{"text":..,"at_ms?":..}]}` |
| `now_playing_seek` | `{"position_ms":N}` | `{"position_ms":..,"duration_ms":..}` (read back after the seek) |
| `now_playing_set_rating` | `{"rating":0-5\|null}` | `{"rating":<new>}` |
| `now_playing_set_lfm` | `{"status":"normal"\|"love"\|"ban"}` | `{"lfm_status":<new>}` |
| `now_playing_set_tag` | `{"tag":"<name>","value":"<v>"}` | `{}` |

`now_playing_details` carries what the canonical track does not, for a details pane:

```json
{
  "track_count": 10, "disc_count": 1,        // int | null
  "play_count": 3, "skip_count": 0,
  "channels": 2, "sample_rate": 44100, "bitrate": 320,
  "publisher": "Label", "composer": "Composer", "comment": "..", "grouping": "..",
  "rating_album": "..", "encoder": "LAME", "kind": "mp3", "format": "MPEG",
  "size": "..", "date_modified": "..", "last_played": ".."   // strings, "" when unknown
}
```

The seven counts parse to integers and are **`null`** when the tag says nothing, so a client
never has to tell "0" apart from "absent". The rest are the host's own strings, passed through
as it formats them.

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

In both views **`total` is the number of tracks the view holds**, not the size of the page:
the whole queue by default, and everything still to play under `up_next`. Page it the same
way in either.

| Op | Request `data` | Response |
|----|----------------|----------|
| `now_playing_list` | `{offset?, limit?, up_next?, totals?}` | `{total, offset, version, items}` - canonical tracks + `order` + `position` + `play_position` |
| `now_playing_list_play` | `{"order":N, "version?":V}` | `{}` |
| `now_playing_list_remove` | `{"orders":[N,..], "version?":V}` | `{}` - removes every listed slot |
| `now_playing_list_move` | `{"from":N,"to":M, "version?":V}` | `{}` - `from`/`to` are `order` values |
| `now_playing_list_clear` | `{"version?":V}` | `{}` - empties the queue |
| `now_playing_list_search` | `{"query":"<text>"}` | `{}` |
| `now_playing_queue` | `{"paths":[..],"mode?":"next"\|"last"\|"now"\|"add_all","play?":"<path>"}` | `{}` |

`totals: true` adds **`total_duration_ms`**, how long what `total` counts runs: the whole
queue in the default view, everything still to play under `up_next`. Opt-in because it is the
answer a window cannot give - it reads the tags of the whole list, where a plain page reads
only the rows it serves. A track the host reports no duration for counts as nothing.

**`version` and the mutation guard.** MusicBee addresses the queue by index alone, so an
`order` only means what you read while the list it came from still holds. Every page carries
the queue's `version`; pass it back on a mutation and a queue that moved in between is refused
`stale_list` instead of hitting the wrong slot - re-read the page and retry. The field is
optional: send none and the mutation is unguarded, as before. `now_playing_list_clear` takes
it too: it is the mutation a client is least able to undo, so a queue that moved since the
page it was read from is worth refusing.

**Removing several tracks.** `now_playing_list_remove` takes the `order`s of every slot to
remove, so a multi-select is one request: send the `order` values exactly as the page listed
them, and the server removes the highest first so no removal shifts a slot still to go. The
batch is checked whole before anything is removed: an empty list, a negative or repeated
`order`, or one past the end of the queue is `invalid_field` and removes nothing. If MusicBee
refuses a removal part way through, the reply is `internal_error`, some slots may already be
gone, and the `version` has moved, so re-read the list.

> `mode` is `snake_case` like every other V6 enum, so the last one is **`add_all`** - V4 spells
> it `add-all` on its own wire, and V6 rejects that spelling. An unrecognized mode is
> `invalid_field`, never silently treated as `next`.

> A single view that is *both* shuffle-play-order *and* keeps already-played tracks is impossible -
> MusicBee's `GetNextIndex` is forward-only, so a played track's play order can't be recovered. The
> `play_position: -1` marker is the answer instead (#118 §7 / #94).

### Library

| Op | Request `data` | Response |
|----|----------------|----------|
| `library_genres` | `{offset?, limit?, query?, sort?}` | page of `{"genre":..,"count":..}` |
| `library_artists` | `{offset?, limit?, query?, sort?, genre?, album_artists?}` | page of `{"artist":..,"count":..}` |
| `library_albums` | `{offset?, limit?, query?, sort?, order?, artist?}` | page of `{"album":..,"artist":..,"count":..}` (+ `cover_hash` when cached, + `year` when the tracks agree on one) |
| `library_tracks` | `{offset?, limit?, query?, sort?, order?, genre?, artist?, album?}` | page of [canonical tracks](#canonical-track) |
| `library_radio` | `{offset?, limit?}` | page of `{"name":..,"url":..}` |
| `library_play_all` | `{shuffle?}` | `{}` |
| `library_queue` | `{mode?, play?, shuffle?, genre?, artist?, album?, query?}` | `{"count":N}` |

**Scope.** `genre`, `artist` and `album` narrow a listing to what they name, and
combine. An **empty string is an answer, not an absence**: `{"artist":""}` names the
tracks filed under no artist at all, which is the only way to reach the untagged
corner of a library. Omit the key entirely to mean "no filter".

On `library_tracks` the artist is not a second filter: it says **which record is
meant** when several share a title, and is ignored without an album. The rule is
that the artist chooses between records that share a title and never trims a
record's contents, so a list and what it queues cannot disagree.

**Order.** `sort` on the name lists (`library_genres`, `library_artists`) takes only
`name`. On `library_albums` and `library_tracks` it takes `title`, `artist`, `album`,
`album_artist`, `track`, `year`, `rating` or `date_added`; `order` is `asc` (default)
or `desc`. An unknown value is `invalid_field` rather than a silent fallback.

**Search.** `query` is a case-insensitive substring match, applied to the whole level
rather than to the page that happens to be loaded. A searched list with no `sort`
comes back **by relevance** - the name that *is* the search before the names merely
containing it - and by the named order once one is asked for.

**`library_queue`** queues what a scope selects without the client naming the tracks:
the server resolves the scope and answers how many it queued. `mode` is `now`, `next`
or `last` (default `last`); `play` names one `src` to start from; `shuffle` shuffles
the selection rather than turning the player's shuffle mode on.

### Playlist

| Op | Request `data` | Response |
|----|----------------|----------|
| `playlist_list` | `{offset?, limit?}` | page of `{"url":..,"name":..,"editable":bool}` |
| `playlist_play` | `{"url":"<path>"}` | `{}` |
| `playlist_tracks` | `{"url":"<path>", offset?, limit?, query?, query_field?, totals?}` | `{name, version, editable, total, offset, items}` |
| `playlist_create` | `{"name":"..", folder?, <tracks>?}` | `{url, name, version}` |
| `playlist_delete` | `{"url":"<path>"}` | `{}` |
| `playlist_add_tracks` | `{"url":"<path>", <tracks>, version?}` | `{version, added}` - appends |
| `playlist_remove_tracks` | `{"url":"<path>", "orders":[N,..], version?}` | `{version, removed}` |
| `playlist_move_tracks` | `{"url":"<path>", "from_orders":[N,..], "to_order":M, version?}` | `{version}` |
| `playlist_set_tracks` | `{"url":"<path>", <tracks>, version?}` | `{version}` - replaces the contents |

`playlist_tracks` answers one playlist as a page of [canonical tracks](#canonical-track),
each carrying two indices:

- **`order`** - its place in the playlist, 0-based. This is the key a mutation takes,
  and it does not change when a search narrows the list.
- **`position`** - its rank among the rows returned. Equal to `order` on an unfiltered
  read, and different under a `query`, which is exactly when a client must not
  renumber anything.

`version` is an opaque token over the ordered paths - a string, because it is a
64-bit hash and no JavaScript number holds one exactly. It changes when the playlist
is reordered, not only when its contents change, and because it is computed from the
contents it also changes when the playlist is edited in MusicBee itself. Clients must
not interpret it.

`query` narrows the whole playlist, `query_field` points it at one column (`any`
default, `title`, `artist`, `album`), and `totals: true` adds **`total_duration_ms`**
summed over whatever `total` counts - so a narrowed list reports its own length.

Both are opt-in because they are the two answers a window cannot give: each reads the
playlist's own tags, where a plain page reads only the tags of the rows it serves.

A path the host cannot describe still comes back as a canonical track with its tags
empty, so a client parses one shape and the row keeps its place in the playlist.

**An unreadable playlist is an empty one.** MusicBee derives a name from the filename
and reports no files, so a read of a path that does not exist answers an empty page
with `editable: false`. Only a missing `url` is an error on a read; an edit or a
`playlist_delete` of such a url is `not_found`, and nothing is written.

#### Editing a playlist

**`editable`** says whether a playlist holds a list of tracks that can be changed. An
auto playlist is a rule MusicBee evaluates, not a list, so every edit of one is
`unavailable`; `playlist_delete` still removes it. Hide edit actions where `editable`
is false rather than offering one that will be refused.

**`<tracks>`** names the tracks to add, set or create with, in exactly one of three ways:

- `"paths":[..]` - the `src` of each track, in order.
- a library scope - `genre`, `artist`, `album` and `query`, exactly as
  [`library_queue`](#library) takes them. The server resolves it, so adding an artist
  never pulls its paths to the client and back. A blank `query` is no scope.
- `"now_playing":true` - the whole queue, in queue order. `playlist_create` with it is
  "save the queue as a playlist".

Naming two of them is `invalid_field`; naming none is `missing_field`, except on
`playlist_create`, where it makes an empty playlist.

**`version` guards every edit** the way it guards the queue: send the `version` of the
page the `order`s came from, and a playlist that changed since - from another client or
in MusicBee - is refused `stale_list` with nothing written. Re-read and retry. Without a
`version` the edit is unguarded. Every edit replies with the new `version`, which is the
one the next `playlist_tracks` serves, so a client can edit again without re-reading.

**Positions are `order` values.** `playlist_remove_tracks` takes the `order`s of every
slot to remove, in any order, so a multi-select is one request; a repeated `order`, or
one past the end, is `invalid_field` and removes nothing. `playlist_move_tracks` lifts
the `from_orders` out, keeping their own order, and puts them back so the first lands at
`to_order` in the result: moving `[0]` to `3` in `a b c d e` gives `b c d a e`.
`to_order` runs from 0 to the playlist's length minus the number moved. Removing and
moving write the whole list in one host call, so a batch costs the same as a single edit.

**`playlist_create`** takes a `name` (not blank, and none of `< > : " / \ | ? *`, since it
becomes a file name) and an optional `folder` under the playlists root, and answers the
new playlist's `url`. `folder` is a relative path, `\` or `/` between its parts: a leading
separator, a drive, a `.` or `..` part, or a part holding those characters is
`invalid_field`.

### Podcast

Browse subscriptions and their episodes, and play one (#37). Additive: nothing here
changes the player, library, playlist or now-playing surface, and playing an episode
is the ordinary queue command with a URL the core resolved.

| Op | Request `data` | Response |
|----|----------------|----------|
| `podcast_subscriptions` | `{offset?, limit?}` | `{total, offset, items}` of `subscription` |
| `podcast_subscription` | `{"id":"<id>"}` | one `subscription` |
| `podcast_episodes` | `{"id":"<id>", offset?, limit?}` | `{total, offset, items}` of `episode` |
| `podcast_episode` | `{"id":"<id>","index":N}` | one `episode` |
| `podcast_episode_play` | `{"id":"<id>","index":N,"mode?":"now"\|"next"\|"last"}` | `{}` |

```json
// subscription
{ "id": "<id>", "title": "Podcast Title", "grouping": "Category", "genre": "Technology",
  "description": "...", "downloaded_count": 10, "episode_count": 79, "image_hash": "<hash>" }

// episode
{ "index": 0, "id": "<feed id>", "title": "Episode Title", "date": "2026-01-15T00:00:00Z",
  "description": "...", "duration_ms": 5025000, "is_downloaded": true, "has_been_played": false,
  "url": "https://feed/episode.mp3", "author": "The Hosts" }
```

**`id` is the feed's address** for a real subscription. MusicBee also reports its own
smart views (`All` = Unplayed Episodes, `Recent` = Recent Updates) as subscriptions, with a
plain word for an id and no artwork; they hold episodes from every show. A client that wants
to tell them apart can test whether the id is a URL.

**`url` says where an episode is now**, not what it is: the feed URL until MusicBee downloads
it, the local path afterwards. Use `(id, index)` to address an episode, never the url.

**`author` comes from the episode's own tags**, not the podcast API, which has no such field.
It is worth showing in the smart views, where every row is a different show; within one feed
it repeats the show itself. Empty when the host cannot resolve the url.

**An episode is addressed by `(id, index)`**, because that is the only key MusicBee takes.
The feed's own `id` is carried so a client can recognise an episode across a re-read, and
opens nothing. Unlike the now-playing queue there is **no version token**: MusicBee announces
nothing about podcasts, so nothing could keep one honest. Acting on an index a feed refresh
has shifted reaches a neighbouring episode, which is all the drift can cost.

`mode` defaults to **`now`**, where the queueing ops default to `next`: asking for one episode
of one podcast is asking to hear it. **Download state does not gate playing.** MusicBee streams
a feed URL exactly as it does from its own window, so a client may play any episode it can see.

`image_hash` is the subscription's artwork in the same content-addressed store album art uses
(`podcast:` namespace), fetched with `cover_get` or `GET /api/cover/{hash}` like any other
cover. It is resolved for the page being served, so listing ten subscriptions never ingests a
hundred, and is absent when a feed has no art. Artwork is per subscription only: MusicBee's
artwork call takes an index, but every index past the feed image returns nothing, so an
episode has no image of its own.

An unknown `id` on `podcast_episodes` answers an empty page rather than `not_found`, because
MusicBee reports a subscription with no episodes the same way it reports one that is not
there. The single-item ops do say `not_found`.

**No events.** MusicBee has no podcast notifications to forward, so nothing is broadcast and a
client re-reads when it wants to be current (#118 §8). The one exception is the playback that
`podcast_episode_play` causes, which surfaces through the ordinary now-playing events.

## Events

Broadcast to every subscribed (non-`no_broadcast`) connection, best effort. Most are marker
events - they carry `{}` (or a small hint like `cover_cache_changed`'s `building`) and mean
"re-query"; the client refetches the relevant op rather than trusting the event payload as state.

| Event | `data` | Fires when |
|-------|--------|-----------|
| `play_state_changed` | `{"play_state":".."}` | playback starts/pauses/stops |
| `volume_changed` | `{"volume":N}` | volume changes |
| `mute_changed` | `{"muted":bool}` | mute toggles |
| `shuffle_changed` | `{"shuffle":".."}` | the shuffle mode changes, including from MusicBee's own window |
| `repeat_changed` | `{"repeat":".."}` | the repeat mode changes |
| `scrobbling_changed` | `{"scrobbling":bool}` | scrobbling is turned on or off |
| `stop_after_current_changed` | `{"stop_after_current":bool}` | stop-after-current is turned on or off, including from MusicBee's own window and when it clears itself after firing |
| `now_playing_changed` | `{"artist":..,"title":..,"album":..,"path":..}` | the track changes |
| `now_playing_lyrics_changed` | `{}` | lyrics finished loading for the current track -> re-query `now_playing_lyrics` |
| `now_playing_list_changed` | `{}` | the queue changed -> re-query `now_playing_list` |
| `cover_cache_changed` | `{"building":bool}` | album-cover cache changed (`building` = a build is in progress vs finished) -> re-resolve `cover_hash` |
| `library_changed` | `{}` | the library changed (add/scan/switch) -> re-browse |
| `server_shutdown` | `{}` | the server is going away deliberately (MusicBee closing, networking stopped) |
| `permissions_changed` | the [`permissions`](#what-a-client-is-told) object | this client's Party Mode permissions changed; sent only to the client concerned |

**Clients MUST ignore events they do not recognize.** The catalog grows additively, so an
unknown `event` name is skipped, never treated as an error. Without this rule no event can
ever be added without breaking a shipped client.

`server_shutdown` is what separates a deliberate stop from a network drop: on receiving it a
client should stop reconnecting until it discovers the server again, rather than retrying into
a MusicBee that is closing. It is best-effort like every other event - a crash or a pulled
cable produces no goodbye, so a dropped connection with no preceding `server_shutdown` still
means "retry".

**Shuffle, repeat and scrobbling are polled, not announced.** MusicBee raises no
notification for any of the three, so the server diffs them once a second and
broadcasts what moved. That is what makes a change someone makes in MusicBee's own
window visible to a client at all, and it is why these three carry their new value
rather than being markers: there is nothing cheaper to re-query.

> There is still no `lfm_changed` event, so the love/ban state refreshes on the next
> `now_playing_state` (or from a setter's reply). A candidate addition, tracked
> against #118.

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
