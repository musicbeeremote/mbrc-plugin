# MusicBee Remote Protocol Documentation

This document describes the communication protocol between MusicBee Remote clients and the plugin.

It documents the **maintained protocol-4 surface** only: the commands that shipping
Android and iOS clients actually use and that we intend to keep supporting. Pre-V4
versions (2 / 2.1 / 3) are legacy and out of scope here (see [Protocol Versions](#protocol-versions)).

## Table of Contents

- [Overview](#overview)
- [Connection Architecture](#connection-architecture)
- [Message Format](#message-format)
- [Protocol Handshake](#protocol-handshake)
- [Client Usage Legend](#client-usage-legend)
- [Quick Reference](#quick-reference)
- [Protocol Versions](#protocol-versions)
- [Commands by Category](#commands-by-category)
- [Broadcast Events](#broadcast-events)
- [Data Models](#data-models)
- [Best Practices](#best-practices)

---

## Overview

MusicBee Remote uses a TCP socket-based protocol with newline-terminated JSON messages.

| Property | Value |
|----------|-------|
| Transport | TCP Socket |
| Default Port | 3000 (configurable) |
| Message Format | JSON |
| Encoding | UTF-8 (no BOM) |
| Message Terminator | CRLF (`\r\n`) |
| Maintained Version | 4 |
| Also | V5 = V4 + `nowplayingcurrentposition` (iOS); V6 = reserved future redesign |

**Important:** All messages must be encoded as UTF-8 without a Byte Order Mark (BOM). The plugin expects and sends UTF-8 encoded text.

---

## Connection Architecture

### Dual Socket Pattern

Clients are recommended to establish **two separate connections** to the plugin:

#### 1. Main Socket (Broadcast-enabled)
- Receives all broadcast events (track changes, player state, etc.)
- Used for real-time UI updates
- Default behavior when connecting

#### 2. Data Socket (No-broadcast)
- Does not receive broadcast events
- Used for heavy data requests (album covers, large lists)
- Prevents broadcast queue buildup during long operations

### Why Use Dual Sockets?

When fetching heavy data like album covers or paginated library requests:
- These operations can take significant time
- During this time, broadcasts continue to queue up on the main socket
- This can cause memory issues and delayed UI updates
- Using a separate no-broadcast socket isolates heavy requests

### Example Connection Pattern

```
┌─────────────────────────────────────────────────────────────┐
│                         Client                               │
├─────────────────────────────────────────────────────────────┤
│  Main Socket (port 3000)          Data Socket (port 3000)   │
│  ├─ Broadcasts: enabled           ├─ Broadcasts: disabled   │
│  ├─ Track changes                 ├─ Album cover requests   │
│  ├─ Player state updates          ├─ Library browsing       │
│  └─ Real-time events              └─ Paginated queries      │
└─────────────────────────────────────────────────────────────┘
```

---

## Message Format

### Request Format

```json
{"context": "command_name", "data": <payload>}\r\n
```

- `context`: Command identifier (string)
- `data`: Command payload (varies by command - can be object, string, number, or null)

Examples below use `data: null` for query/action requests, but the plugin **ignores the
request `data` for getters and actions** - real clients send `null`, `""`, or `true`
interchangeably (e.g. the Android client often sends `true`, the iOS client `""`). Only
commands that set a value read the request `data`.

### Response Format

```json
{"context": "command_name", "data": <response_payload>}\r\n
```

### Examples

**Simple command (no data):**
```json
{"context": "playernext", "data": null}
```

**Command with numeric data:**
```json
{"context": "playervolume", "data": 75}
```

**Command with object data:**
```json
{
  "context": "nowplayingqueue",
  "data": {
    "queue": "next",
    "data": ["file:///path/to/song.mp3"]
  }
}
```

---

## Protocol Handshake

Every client performs a protocol handshake after connecting. This declares the client's protocol version and capabilities.

### Context
`protocol`

### Request

```json
{
  "context": "protocol",
  "data": {
    "protocol_version": 4,
    "no_broadcast": false,
    "client_id": "MyApp"
  }
}
```

### Handshake Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `protocol_version` | int | Client's protocol version (`4`, or `5` for the iOS current-position extension) |
| `no_broadcast` | bool | If `true`, this connection will not receive broadcasts |
| `client_id` | string | Optional client identifier for logging |

### Response

```json
{
  "context": "protocol",
  "data": 4
}
```

The server responds with its protocol version. The effective protocol is the minimum of client and server versions.

### Setting Up Dual Sockets

**Main socket handshake:**
```json
{
  "context": "protocol",
  "data": {
    "protocol_version": 4,
    "no_broadcast": false
  }
}
```

**Data socket handshake:**
```json
{
  "context": "protocol",
  "data": {
    "protocol_version": 4,
    "no_broadcast": true
  }
}
```

Before the `protocol` frame, a client first identifies its platform with a `player` frame
(`"Android"` / `"iOS"`); the plugin uses this to apply per-platform wire quirks.

---

## Client Usage Legend

Every command below is tagged with the clients that use it, verified against the Android
client source (`Protocol.kt`, v1.1.0-v1.6.1), the iOS protocol sheet, and captured golden
traces:

| Tag | Meaning |
|-----|---------|
| **Both** | Sent by both the Android and iOS clients |
| **Android** | Sent by the Android client only |
| **iOS** | Sent by the iOS client only |

---

## Quick Reference

All maintained V4 contexts, organized by category.

### System

| Context | Description | Clients | Since |
|---------|-------------|---------|-------|
| `protocol` | Protocol version handshake (extended object format) | Both | V4 |
| `player` | Client platform identification (`"Android"` / `"iOS"`) | Both | V4 |
| `ping` | Keepalive ping (server → client) | Both | V4 |
| `pong` | Keepalive pong (client → server) | Both | V4 |
| `pluginversion` | Query the plugin version string | Both | V4 |
| `init` | Request initial state sync (triggers a bundle of responses) | Both | V4 |
| `verifyconnection` | Verify the connection is active (answered pre-auth) | Both | V4 |

### Player Control

| Context | Description | Clients | Since |
|---------|-------------|---------|-------|
| `playerplaypause` | Toggle play/pause state | Both | V4 |
| `playerplay` | Start playback (media-session / lockscreen) | Android | V4 |
| `playerpause` | Pause playback (media-session / lockscreen) | Android | V4 |
| `playerstop` | Stop playback | Android | V4 |
| `playernext` | Skip to next track | Both | V4 |
| `playerprevious` | Skip to previous track | Both | V4 |
| `playervolume` | Get or set volume level (0-100) | Both | V4 |
| `playermute` | Get, set, or toggle mute state | Android | V4 |
| `playershuffle` | Get, set, or toggle shuffle mode | Both | V4 |
| `playerrepeat` | Get, set, or toggle repeat mode (None/All/One) | Both | V4 |
| `scrobbler` | Get, set, or toggle Last.fm scrobbling | Android | V4 |
| `playerstatus` | Full player status (state, volume, modes) | Both | V4 |
| `playeroutput` | Get or set audio output device | Android | V4 |
| `playeroutputswitch` | Switch to a specific output device | Android | V4 |

### Now Playing Track

| Context | Description | Clients | Since |
|---------|-------------|---------|-------|
| `nowplayingtrack` | Current track info (artist, title, album, year, path) | Both | V4 |
| `nowplayingdetails` | Extended track metadata (genre, bitrate, etc.) | Both | V4 |
| `nowplayingposition` | Get or set playback position in milliseconds | Both | V4 |
| `nowplayingcurrentposition` | Lightweight current-position poll (replies on `nowplayingposition`) | iOS | **V5** |
| `nowplayingcover` | Album artwork for current track | Both | V4 |
| `nowplayinglyrics` | Lyrics for current track | Both | V4 |
| `nowplayingrating` | Get or set track rating | Both | V4 |
| `nowplayinglfmrating` | Get or set Last.fm love/ban status | Both | V4 |
| `nowplayingtagchange` | Modify track metadata tags | iOS | V4 |

### Now Playing List

| Context | Description | Clients | Since |
|---------|-------------|---------|-------|
| `nowplayinglist` | Get the now playing queue (paginated) | Both | V4 |
| `nowplayinglistplay` | Play a specific track from the queue by index | Both | V4 |
| `nowplayinglistremove` | Remove a track from the queue by index | Both | V4 |
| `nowplayinglistmove` | Move a track within the queue | Both | V4 |
| `nowplayinglistsearch` | Search and play a track in the queue | Android | V4 (removed in Android 1.6.0) |
| `nowplayingqueue` | Queue files to the now playing list | Both | V4 |

### Library Browse (flat, paginated)

| Context | Description | Clients | Since |
|---------|-------------|---------|-------|
| `browsegenres` | Browse all genres (paginated) | Both | V4 |
| `browseartists` | Browse all artists (paginated, supports album artists) | Both | V4 |
| `browsealbums` | Browse all albums (paginated) | Both | V4 |
| `browsetracks` | Browse all tracks (paginated) | Both | V4 |

### Library Navigation (hierarchical, by name)

| Context | Description | Clients | Since |
|---------|-------------|---------|-------|
| `librarygenreartists` | Get all artists in a specific genre | iOS | V4 |
| `libraryartistalbums` | Get all albums by a specific artist | iOS | V4 |
| `libraryalbumtracks` | Get all tracks on a specific album | iOS | V4 |

### Library Covers & Misc

| Context | Description | Clients | Since |
|---------|-------------|---------|-------|
| `libraryalbumcover` | Get album cover by artist/album (paginated) | Both | V4 |
| `librarycovercachebuildstatus` | Query cover cache build progress | iOS | V4 |
| `libraryplayall` | Play entire library (optional shuffle) | Both | V4 |
| `radiostations` | Get available radio stations (paginated) | Android | V4 |

### Playlists

| Context | Description | Clients | Since |
|---------|-------------|---------|-------|
| `playlistlist` | Get all playlists (paginated) | Both | V4 |
| `playlistplay` | Play a specific playlist by URL | Both | V4 |

### Error Responses

| Context | Description |
|---------|-------------|
| `error` | Error occurred processing a request |
| `notallowed` | Operation not permitted (authentication required) |

---

## Protocol Versions

### Version 4 (maintained baseline)

The maintained protocol. Every command in this document is V4 unless explicitly noted. V4
uses object payloads, pagination, string-typed player fields, and the full player / now
playing / library / playlist surface. It is the minimum version new clients should target.

### Version 5 (legacy extension)

V5 is V4 plus a **single** addition: `nowplayingcurrentposition`, a lightweight
current-position poll. The iOS client sends it only when the server advertises
`protocol_version >= 5`; the handler replies on the existing `nowplayingposition` context.
There are no other differences from V4. (Origin: the plugin's `old-develop` branch.)

### Version 6 (reserved, future)

Reserved for a future strict/standardized redesign: consistent typing, snake_case keys, a
uniform error envelope, and a traceability envelope for request/response correlation. Not
yet defined and not implemented. All new schema work targets V6, not V4/V5.

### Legacy (V2 / V2.1 / V3)

Superseded and out of scope for this document and the Rust core. The C# plugin still
negotiates them for old installs, but no maintained client uses them and their payload
shapes are not documented here.

> **Not documented / not in the Rust core:** `librarysearch{title,artist,album,genre}`,
> `libraryqueue{track,artist,album,genre}`, and `playerautodj` are pre-V4 legacy commands
> that no shipping Android or iOS client sends. They remain registered in the C# plugin as
> a compatibility safety net but are intentionally excluded from this reference and from the
> Rust core.

---

## Commands by Category

### System Commands

#### Protocol Handshake
| Property | Value |
|----------|-------|
| Context | `protocol` |
| Clients | Both |
| Broadcast | No |

**Request:**
```json
{
  "context": "protocol",
  "data": {
    "protocol_version": 4,
    "no_broadcast": false,
    "client_id": "MyApp"
  }
}
```

**Response:**
```json
{
  "context": "protocol",
  "data": 4
}
```

---

#### Player Identification
| Property | Value |
|----------|-------|
| Context | `player` |
| Clients | Both |
| Broadcast | No |

Sent right after connecting, before the `protocol` handshake, to declare the client platform.

**Request:**
```json
{
  "context": "player",
  "data": "Android"
}
```

`data` is the platform string (`"Android"` / `"iOS"`). The plugin uses it to apply
per-platform wire quirks (e.g. lenient parsing for the iOS client).

---

#### Ping / Pong (Keepalive)
| Property | Value |
|----------|-------|
| Context | `ping` (server → client), `pong` (client → server) |
| Clients | Both |
| Broadcast | No |

The server periodically sends `ping`; the client replies `pong`. Both carry an empty-string
`data` on the wire.

```json
{"context": "ping", "data": ""}
{"context": "pong", "data": ""}
```

---

#### Plugin Version
| Property | Value |
|----------|-------|
| Context | `pluginversion` |
| Clients | Both |
| Broadcast | No |

**Request:**
```json
{
  "context": "pluginversion",
  "data": null
}
```

**Response:**
```json
{
  "context": "pluginversion",
  "data": "1.4.0"
}
```

---

#### Initial State Sync
| Property | Value |
|----------|-------|
| Context | `init` |
| Clients | Both |
| Broadcast | No |

The V4 handshake state mechanism. On `init` the plugin pushes a bundle of state to the
requesting client (`nowplayingtrack`, `nowplayingrating`, `nowplayinglfmrating`,
`playerstatus`) and broadcasts `nowplayingcover` / `nowplayinglyrics`. This subsumes the
individual state requests, so a V4 client does not request them separately.

**Request:**
```json
{
  "context": "init",
  "data": null
}
```

---

#### Verify Connection
| Property | Value |
|----------|-------|
| Context | `verifyconnection` |
| Clients | Both |
| Broadcast | No |

Connection health check. Handled **before** the authentication gate: the plugin echoes it
back with empty data. Timer/reconnect driven, not user-triggered.

**Request:**
```json
{
  "context": "verifyconnection",
  "data": null
}
```

**Response:**
```json
{
  "context": "verifyconnection",
  "data": ""
}
```

---

### Player Control Commands

#### Play / Pause / Stop / Play-Pause Toggle
| Property | Value |
|----------|-------|
| Context | `playerplay`, `playerpause`, `playerstop`, `playerplaypause` |
| Clients | `playerplaypause` **Both**; `playerplay` / `playerpause` / `playerstop` **Android** |
| Broadcast | Yes (`playerstate`) |

In-app and notification buttons collapse to `playerplaypause`. The separate `playerplay` /
`playerpause` split only fires from the Android media session (Bluetooth / Android Auto /
lockscreen). iOS uses `playerplaypause` only.

**Request:**
```json
{
  "context": "playerplaypause",
  "data": null
}
```

**Response:**
```json
{
  "context": "playerplaypause",
  "data": true
}
```

---

#### Next / Previous
| Property | Value |
|----------|-------|
| Context | `playernext`, `playerprevious` |
| Clients | Both |
| Broadcast | Yes (`nowplayingtrack`, etc.) |

**Request:**
```json
{
  "context": "playernext",
  "data": null
}
```

**Response:**
```json
{
  "context": "playernext",
  "data": true
}
```

---

#### Volume
| Property | Value |
|----------|-------|
| Context | `playervolume` |
| Clients | Both |
| Broadcast | Yes |

**Set volume:**
```json
{
  "context": "playervolume",
  "data": 75
}
```

**Query volume:**
```json
{
  "context": "playervolume",
  "data": null
}
```

**Response** (integer; note: volume is stringified only inside `playerstatus`, not here):
```json
{
  "context": "playervolume",
  "data": 75
}
```

---

#### Mute
| Property | Value |
|----------|-------|
| Context | `playermute` |
| Clients | Android |
| Broadcast | Yes |

**Set / toggle / query:**
```json
{"context": "playermute", "data": true}
{"context": "playermute", "data": "toggle"}
{"context": "playermute", "data": null}
```

**Response:**
```json
{
  "context": "playermute",
  "data": false
}
```

---

#### Shuffle
| Property | Value |
|----------|-------|
| Context | `playershuffle` |
| Clients | Both |
| Broadcast | Yes |

**Toggle / set:**
```json
{"context": "playershuffle", "data": "toggle"}
{"context": "playershuffle", "data": "shuffle"}
```

**Response:**
```json
{
  "context": "playershuffle",
  "data": "shuffle"
}
```

Shuffle states: `"off"`, `"shuffle"`, `"autodj"`

---

#### Repeat
| Property | Value |
|----------|-------|
| Context | `playerrepeat` |
| Clients | Both |
| Broadcast | Yes |

**Toggle (cycles None → All → One → None) / set:**
```json
{"context": "playerrepeat", "data": "toggle"}
{"context": "playerrepeat", "data": "All"}
```

**Response:**
```json
{
  "context": "playerrepeat",
  "data": "All"
}
```

Repeat modes: `"None"`, `"All"`, `"One"`

---

#### Scrobbling
| Property | Value |
|----------|-------|
| Context | `scrobbler` |
| Clients | Android |
| Broadcast | Yes |

**Toggle / query:**
```json
{"context": "scrobbler", "data": "toggle"}
{"context": "scrobbler", "data": null}
```

**Response:**
```json
{
  "context": "scrobbler",
  "data": true
}
```

---

#### Player Status (Full State)
| Property | Value |
|----------|-------|
| Context | `playerstatus` |
| Clients | Both |
| Broadcast | Yes |

Sent to each client as part of the `init` bundle, and broadcast on state change.

**Request:**
```json
{
  "context": "playerstatus",
  "data": null
}
```

**Response:**
```json
{
  "context": "playerstatus",
  "data": {
    "playerstate": "Playing",
    "playervolume": "75",
    "playermute": false,
    "playershuffle": "off",
    "playerrepeat": "None",
    "scrobbler": true
  }
}
```

---

#### Output Device
| Property | Value |
|----------|-------|
| Context | `playeroutput` |
| Clients | Android |
| Broadcast | Yes |

**Query devices:**
```json
{
  "context": "playeroutput",
  "data": null
}
```

**Set device:**
```json
{
  "context": "playeroutput",
  "data": "Speakers"
}
```

**Response:**
```json
{
  "context": "playeroutput",
  "data": {
    "active": "Speakers",
    "devices": ["Speakers", "Headphones", "HDMI Output"]
  }
}
```

---

#### Output Device Switch
| Property | Value |
|----------|-------|
| Context | `playeroutputswitch` |
| Clients | Android |
| Broadcast | Yes |

Switches to a specific output device by name. Responds on the `playeroutput` context.

**Request:**
```json
{
  "context": "playeroutputswitch",
  "data": "Headphones"
}
```

**Response:**
```json
{
  "context": "playeroutput",
  "data": {
    "active": "Headphones",
    "devices": ["Speakers", "Headphones", "HDMI Output"]
  }
}
```

---

### Now Playing Commands

#### Track Info
| Property | Value |
|----------|-------|
| Context | `nowplayingtrack` |
| Clients | Both |
| Broadcast | Yes (on track change) |

**Request:**
```json
{
  "context": "nowplayingtrack",
  "data": null
}
```

**Response:**
```json
{
  "context": "nowplayingtrack",
  "data": {
    "artist": "Artist Name",
    "title": "Song Title",
    "album": "Album Name",
    "year": "2024",
    "path": "C:\\Music\\song.mp3"
  }
}
```

---

#### Track Details
| Property | Value |
|----------|-------|
| Context | `nowplayingdetails` |
| Clients | Both |
| Broadcast | No |

**Request:**
```json
{
  "context": "nowplayingdetails",
  "data": null
}
```

**Response** (all values are strings):
```json
{
  "context": "nowplayingdetails",
  "data": {
    "albumArtist": "Album Artist",
    "genre": "Rock",
    "trackNo": "5",
    "trackCount": "12",
    "discNo": "1",
    "discCount": "1",
    "publisher": "Record Label",
    "composer": "Composer Name",
    "comment": "",
    "grouping": "",
    "ratingAlbum": "",
    "encoder": "LAME",
    "kind": "mp3",
    "format": "MPEG-1 Layer 3",
    "size": "8542631",
    "channels": "2",
    "sampleRate": "44100",
    "bitrate": "320",
    "dateModified": "2024-01-15",
    "dateAdded": "2024-01-01",
    "lastPlayed": "2024-01-20",
    "playCount": "15",
    "skipCount": "2",
    "duration": "245000"
  }
}
```

---

#### Playback Position
| Property | Value |
|----------|-------|
| Context | `nowplayingposition` |
| Clients | Both |
| Broadcast | Yes (on seek) |

**Query position:**
```json
{
  "context": "nowplayingposition",
  "data": null
}
```

**Seek to position (milliseconds):**
```json
{
  "context": "nowplayingposition",
  "data": 120000
}
```

**Response:**
```json
{
  "context": "nowplayingposition",
  "data": {
    "current": 120000,
    "total": 245000
  }
}
```

---

#### Current Position (V5)
| Property | Value |
|----------|-------|
| Context | `nowplayingcurrentposition` |
| Clients | iOS |
| Since | **V5** |
| Broadcast | No |

A lightweight poll for the elapsed position only. The iOS client sends it **only when the
server advertised `protocol_version >= 5`**; against a V4 server it is never sent. The
handler replies on the `nowplayingposition` context (same payload shape), so there is no
distinct response context.

**Request:**
```json
{
  "context": "nowplayingcurrentposition",
  "data": null
}
```

**Response:**
```json
{
  "context": "nowplayingposition",
  "data": {
    "current": 120000,
    "total": 245000
  }
}
```

---

#### Album Cover
| Property | Value |
|----------|-------|
| Context | `nowplayingcover` |
| Clients | Both |
| Broadcast | Yes (on track change) |

**Request:**
```json
{
  "context": "nowplayingcover",
  "data": null
}
```

**Response:**
```json
{
  "context": "nowplayingcover",
  "data": {
    "status": 200,
    "cover": "base64_encoded_image_data"
  }
}
```

**Status codes:**
- `200` - Cover available and included
- `404` - Cover not found
- `1` - Cover ready (not included in this response; request again)

---

#### Lyrics
| Property | Value |
|----------|-------|
| Context | `nowplayinglyrics` |
| Clients | Both |
| Broadcast | Yes (on track change) |

**Request:**
```json
{
  "context": "nowplayinglyrics",
  "data": null
}
```

**Response:**
```json
{
  "context": "nowplayinglyrics",
  "data": {
    "status": 200,
    "lyrics": "Lyrics text here..."
  }
}
```

Status `404` with empty `lyrics` when none are available.

---

#### Track Rating
| Property | Value |
|----------|-------|
| Context | `nowplayingrating` |
| Clients | Both |
| Broadcast | Yes |

**Set / clear / query:**
```json
{"context": "nowplayingrating", "data": "4"}
{"context": "nowplayingrating", "data": ""}
{"context": "nowplayingrating", "data": null}
```

**Response:**
```json
{
  "context": "nowplayingrating",
  "data": "4"
}
```

Rating is a string `"0"`-`"5"`.

---

#### Last.fm Love/Ban
| Property | Value |
|----------|-------|
| Context | `nowplayinglfmrating` |
| Clients | Both |
| Broadcast | Yes |

**Toggle / set:**
```json
{"context": "nowplayinglfmrating", "data": "toggle"}
{"context": "nowplayinglfmrating", "data": "love"}
{"context": "nowplayinglfmrating", "data": "ban"}
```

**Response:**
```json
{
  "context": "nowplayinglfmrating",
  "data": "Love"
}
```

Values: `"Normal"`, `"Love"`, `"Ban"`

---

#### Tag Change
| Property | Value |
|----------|-------|
| Context | `nowplayingtagchange` |
| Clients | iOS |
| Broadcast | No |

Modifies a metadata tag on the current track. Returns updated track details on the
`nowplayingdetails` context (see [Track Details](#track-details) for the payload shape).

**Request:**
```json
{
  "context": "nowplayingtagchange",
  "data": {
    "tag": "artist",
    "value": "New Artist Name"
  }
}
```

---

### Now Playing List Commands

#### Get List
| Property | Value |
|----------|-------|
| Context | `nowplayinglist` |
| Clients | Both |
| Broadcast | No |

**Request:**
```json
{
  "context": "nowplayinglist",
  "data": {
    "offset": 0,
    "limit": 100
  }
}
```

**Response:**
```json
{
  "context": "nowplayinglist",
  "data": {
    "offset": 0,
    "limit": 100,
    "total": 250,
    "data": [
      {"artist": "Artist", "title": "Song 1", "path": "C:\\Music\\song1.mp3", "position": 0}
    ]
  }
}
```

The iOS client receives two extra per-item fields, `album` and `album_artist` (see
[NowPlayingListTrack](#nowplayinglisttrack)).

---

#### Play Track from List
| Property | Value |
|----------|-------|
| Context | `nowplayinglistplay` |
| Clients | Both |
| Broadcast | Yes (`nowplayingtrack`, etc.) |

**Request** (track index):
```json
{
  "context": "nowplayinglistplay",
  "data": 5
}
```

Clients typically re-request `nowplayinglist` after this to refresh.

**Response:**
```json
{
  "context": "nowplayinglistplay",
  "data": true
}
```

---

#### Remove Track from List
| Property | Value |
|----------|-------|
| Context | `nowplayinglistremove` |
| Clients | Both |
| Broadcast | Yes (`nowplayinglistchanged`) |

**Request** (track index):
```json
{
  "context": "nowplayinglistremove",
  "data": 5
}
```

**Response:**
```json
{
  "context": "nowplayinglistremove",
  "data": {
    "success": true,
    "index": 5
  }
}
```

---

#### Move Track in List
| Property | Value |
|----------|-------|
| Context | `nowplayinglistmove` |
| Clients | Both |
| Broadcast | Yes (`nowplayinglistchanged`) |

**Request:**
```json
{
  "context": "nowplayinglistmove",
  "data": {
    "from": 5,
    "to": 2
  }
}
```

**Response:**
```json
{
  "context": "nowplayinglistmove",
  "data": {
    "success": true,
    "from": 5,
    "to": 2
  }
}
```

---

#### Search Now Playing
| Property | Value |
|----------|-------|
| Context | `nowplayinglistsearch` |
| Clients | Android |
| Broadcast | Yes (if a match is found and played) |

Sent by Android clients up to v1.5.1; removed in Android 1.6.0 (moved client-side). Still a
valid V4 command the plugin answers.

**Request:**
```json
{
  "context": "nowplayinglistsearch",
  "data": "song title"
}
```

**Response:**
```json
{
  "context": "nowplayinglistsearch",
  "data": true
}
```

---

#### Queue Files
| Property | Value |
|----------|-------|
| Context | `nowplayingqueue` |
| Clients | Both |
| Broadcast | Yes (`nowplayinglistchanged`) |

**Request:**
```json
{
  "context": "nowplayingqueue",
  "data": {
    "queue": "next",
    "play": null,
    "data": ["file:///path/to/song1.mp3", "file:///path/to/song2.mp3"]
  }
}
```

Queue types:
- `"now"` / `"playnow"` - Play immediately
- `"next"` - Queue as next track
- `"last"` - Queue at end
- `"add-all"` / `"addandplay"` - Add all and start playing

**Response:**
```json
{
  "context": "nowplayingqueue",
  "data": {
    "code": 200
  }
}
```

Response codes: `200` (success), `400` (invalid request), `500` (error)

---

### Library Commands

#### Browse Genres
| Property | Value |
|----------|-------|
| Context | `browsegenres` |
| Clients | Both |
| Broadcast | No |

**Request:**
```json
{
  "context": "browsegenres",
  "data": {
    "offset": 0,
    "limit": 100
  }
}
```

**Response:**
```json
{
  "context": "browsegenres",
  "data": {
    "offset": 0,
    "limit": 100,
    "total": 45,
    "data": [
      {"genre": "Rock", "count": 150},
      {"genre": "Pop", "count": 85}
    ]
  }
}
```

---

#### Browse Artists
| Property | Value |
|----------|-------|
| Context | `browseartists` |
| Clients | Both |
| Broadcast | No |

**Request:**
```json
{
  "context": "browseartists",
  "data": {
    "offset": 0,
    "limit": 100,
    "album_artists": false
  }
}
```

Set `album_artists: true` to browse album artists instead of track artists.

**Response:**
```json
{
  "context": "browseartists",
  "data": {
    "offset": 0,
    "limit": 100,
    "total": 250,
    "data": [
      {"artist": "Artist Name", "count": 25}
    ]
  }
}
```

---

#### Browse Albums
| Property | Value |
|----------|-------|
| Context | `browsealbums` |
| Clients | Both |
| Broadcast | No |

**Request:**
```json
{
  "context": "browsealbums",
  "data": {
    "offset": 0,
    "limit": 100
  }
}
```

**Response:**
```json
{
  "context": "browsealbums",
  "data": {
    "offset": 0,
    "limit": 100,
    "total": 500,
    "data": [
      {"album": "Album Name", "artist": "Artist Name", "count": 12}
    ]
  }
}
```

---

#### Browse Tracks
| Property | Value |
|----------|-------|
| Context | `browsetracks` |
| Clients | Both |
| Broadcast | No |

**Request:**
```json
{
  "context": "browsetracks",
  "data": {
    "offset": 0,
    "limit": 100
  }
}
```

**Response:**
```json
{
  "context": "browsetracks",
  "data": {
    "offset": 0,
    "limit": 100,
    "total": 5000,
    "data": [
      {
        "src": "C:\\Music\\song.mp3",
        "artist": "Artist Name",
        "title": "Song Title",
        "trackno": 1,
        "disc": 1,
        "album": "Album Name",
        "album_artist": "Album Artist",
        "genre": "Rock"
      }
    ]
  }
}
```

---

#### Get Genre Artists
| Property | Value |
|----------|-------|
| Context | `librarygenreartists` |
| Clients | iOS |
| Broadcast | No |

Hierarchical (name-keyed, non-paginated) navigation used by the iOS client to drill
genre → artists → albums → tracks.

**Request:**
```json
{
  "context": "librarygenreartists",
  "data": "Genre Name"
}
```

**Response:**
```json
{
  "context": "librarygenreartists",
  "data": [
    {"artist": "Artist 1", "count": 25},
    {"artist": "Artist 2", "count": 18}
  ]
}
```

---

#### Get Artist Albums
| Property | Value |
|----------|-------|
| Context | `libraryartistalbums` |
| Clients | iOS |
| Broadcast | No |

**Request:**
```json
{
  "context": "libraryartistalbums",
  "data": "Artist Name"
}
```

**Response:**
```json
{
  "context": "libraryartistalbums",
  "data": [
    {"album": "Album 1", "artist": "Artist Name", "count": 12},
    {"album": "Album 2", "artist": "Artist Name", "count": 8}
  ]
}
```

---

#### Get Album Tracks
| Property | Value |
|----------|-------|
| Context | `libraryalbumtracks` |
| Clients | iOS |
| Broadcast | No |

**Request:**
```json
{
  "context": "libraryalbumtracks",
  "data": "Album Name"
}
```

**Response** (album-track items omit `album`/`genre`):
```json
{
  "context": "libraryalbumtracks",
  "data": [
    {
      "src": "C:\\Music\\track1.mp3",
      "artist": "Artist Name",
      "title": "Track 1",
      "trackno": 1,
      "disc": 1,
      "album_artist": "Album Artist"
    }
  ]
}
```

---

#### Play All Library
| Property | Value |
|----------|-------|
| Context | `libraryplayall` |
| Clients | Both |
| Broadcast | Yes |

**Request** (`true` = shuffle, `false` = in order):
```json
{
  "context": "libraryplayall",
  "data": true
}
```

**Response:**
```json
{
  "context": "libraryplayall",
  "data": true
}
```

---

#### Album Cover
| Property | Value |
|----------|-------|
| Context | `libraryalbumcover` |
| Clients | Both |
| Broadcast | No |

Use this on the **data socket** (no-broadcast) for heavy cover fetching.

**Single cover request:**
```json
{
  "context": "libraryalbumcover",
  "data": {
    "artist": "Artist Name",
    "album": "Album Name",
    "hash": "previous_hash_if_cached",
    "size": 300
  }
}
```

**Paginated cover request:**
```json
{
  "context": "libraryalbumcover",
  "data": {
    "offset": 0,
    "limit": 20
  }
}
```

**Single-cover response:**
```json
{
  "context": "libraryalbumcover",
  "data": {
    "status": 200,
    "artist": "Artist Name",
    "album": "Album Name",
    "cover": "base64_data",
    "hash": "sha1_hash"
  }
}
```

When only a status applies (cover not found, not modified, or cache still building), the
response carries the status alone:
```json
{
  "context": "libraryalbumcover",
  "data": {"status": 404}
}
```

**Paginated response** (for the paginated request form) - a `Page` of cover items:
```json
{
  "context": "libraryalbumcover",
  "data": {
    "offset": 0,
    "limit": 20,
    "total": 120,
    "data": [
      {"album": "Album Name", "artist": "Artist Name", "cover": "base64_data", "status": 200, "hash": "sha1_hash"}
    ]
  }
}
```

Status codes:
- `200` - Cover available
- `304` - Not modified (hash matches)
- `400` - Invalid request (empty album)
- `404` - Cover not found

---

#### Cover Cache Status
| Property | Value |
|----------|-------|
| Context | `librarycovercachebuildstatus` |
| Clients | iOS |
| Broadcast | Yes |

Returned before an album-cover page while the cache is still building; clients retry after.

**Request:**
```json
{
  "context": "librarycovercachebuildstatus",
  "data": null
}
```

**Response** (`true` if the cache is currently being built):
```json
{
  "context": "librarycovercachebuildstatus",
  "data": true
}
```

---

#### Radio Stations
| Property | Value |
|----------|-------|
| Context | `radiostations` |
| Clients | Android |
| Broadcast | No |

**Request:**
```json
{
  "context": "radiostations",
  "data": {
    "offset": 0,
    "limit": 50
  }
}
```

**Response:**
```json
{
  "context": "radiostations",
  "data": {
    "offset": 0,
    "limit": 50,
    "total": 25,
    "data": [
      {"name": "Station Name", "url": "http://stream.url/radio"}
    ]
  }
}
```

---

### Playlist Commands

#### Get Playlists
| Property | Value |
|----------|-------|
| Context | `playlistlist` |
| Clients | Both |
| Broadcast | Yes (on change) |

**Request:**
```json
{
  "context": "playlistlist",
  "data": {
    "offset": 0,
    "limit": 100
  }
}
```

**Response:**
```json
{
  "context": "playlistlist",
  "data": {
    "offset": 0,
    "limit": 100,
    "total": 15,
    "data": [
      {"name": "Favorites", "url": "playlist://favorites"},
      {"name": "Rock Mix", "url": "playlist://rock-mix"}
    ]
  }
}
```

---

#### Play Playlist
| Property | Value |
|----------|-------|
| Context | `playlistplay` |
| Clients | Both |
| Broadcast | Yes |

**Request:**
```json
{
  "context": "playlistplay",
  "data": "playlist_url_or_path"
}
```

**Response:**
```json
{
  "context": "playlistplay",
  "data": true
}
```

---

## Broadcast Events

Broadcasts are automatically sent to all connected clients (unless `no_broadcast: true` was
set during handshake). Both Android and iOS clients consume them.

### Player State Broadcasts

| Context | Trigger | Data |
|---------|---------|------|
| `playerstate` | Play/pause/stop | `"Playing"`, `"Paused"`, `"Stopped"` |
| `playervolume` | Volume change | Integer (0-100) |
| `playermute` | Mute toggle | Boolean |
| `playershuffle` | Shuffle change | ShuffleState (`"off"`/`"shuffle"`/`"autodj"`) |
| `playerrepeat` | Repeat change | `"None"`, `"All"`, `"One"` |
| `scrobbler` | Scrobbler toggle | Boolean |
| `playeroutput` | Output device change | OutputDevice object |

### Track Broadcasts

| Context | Trigger | Data |
|---------|---------|------|
| `nowplayingtrack` | Track change | NowPlayingTrack object |
| `nowplayingposition` | Seek | Position object |
| `nowplayingcover` | Cover available | CoverPayload |
| `nowplayinglyrics` | Lyrics available | LyricsPayload |
| `nowplayingrating` | Rating change | String |
| `nowplayinglfmrating` | Last.fm status change | LastfmStatus |

### List Broadcasts

| Context | Trigger | Data |
|---------|---------|------|
| `nowplayinglistchanged` | List modified | Notification only |
| `playlistlist` | Playlists changed | Updated list |

### Example Broadcast Sequence (Track Change)

When a track changes, clients receive:

```json
{
  "context": "nowplayingtrack",
  "data": {
    "artist": "...",
    "title": "...",
    "album": "...",
    "year": "...",
    "path": "..."
  }
}
{
  "context": "nowplayingposition",
  "data": {
    "current": 0,
    "total": 245000
  }
}
{
  "context": "nowplayingcover",
  "data": {
    "status": 200,
    "cover": "base64..."
  }
}
{
  "context": "nowplayinglyrics",
  "data": {
    "status": 200,
    "lyrics": "..."
  }
}
{
  "context": "nowplayingrating",
  "data": "4"
}
{
  "context": "nowplayinglfmrating",
  "data": "Normal"
}
```

---

## Data Models

### Enumerations

#### PlayState
```
"Undefined" | "Stopped" | "Playing" | "Paused"
```

#### RepeatMode
```
"Undefined" | "None" | "All" | "One"
```

#### ShuffleState
```
"off" | "shuffle" | "autodj"
```

#### LastfmStatus
```
"Normal" | "Love" | "Ban"
```

### Objects

#### PlayerStatus
```json
{
  "playerstate": "Playing",
  "playervolume": "75",
  "playermute": false,
  "playershuffle": "off",
  "playerrepeat": "None",
  "scrobbler": true
}
```

#### NowPlayingTrack
```json
{
  "artist": "string",
  "title": "string",
  "album": "string",
  "year": "string",
  "path": "string"
}
```

#### CoverPayload
```json
{
  "status": 200,
  "cover": "base64_string"
}
```

Note: `cover` field is omitted when null (status 1 or 404).

#### LyricsPayload
```json
{
  "status": 200,
  "lyrics": "string"
}
```

#### PlaybackPosition
```json
{
  "current": 120000,
  "total": 245000
}
```

#### Page (Paginated Response)
```json
{
  "offset": 0,
  "limit": 100,
  "total": 500,
  "data": [...]
}
```

#### Track
```json
{
  "src": "C:\\Music\\song.mp3",
  "artist": "string",
  "title": "string",
  "trackno": 1,
  "disc": 1,
  "album": "string",
  "album_artist": "string",
  "genre": "string"
}
```

#### NowPlayingListTrack
```json
{
  "artist": "string",
  "title": "string",
  "path": "string",
  "position": 0
}
```

The iOS client receives two additional fields, `album` and `album_artist` (empty strings
when unset):
```json
{
  "artist": "string",
  "album": "string",
  "album_artist": "string",
  "title": "string",
  "path": "string",
  "position": 0
}
```

#### GenreData
```json
{
  "genre": "string",
  "count": 0
}
```

#### ArtistData
```json
{
  "artist": "string",
  "count": 0
}
```

#### AlbumData
```json
{
  "album": "string",
  "artist": "string",
  "count": 0
}
```

#### Playlist
```json
{
  "url": "string",
  "name": "string"
}
```

#### RadioStation
```json
{
  "name": "string",
  "url": "string"
}
```

---

## Best Practices

### 1. Use Dual Sockets

```
Main Socket (broadcasts enabled):
- Real-time player state updates
- Track change notifications
- UI synchronization

Data Socket (no_broadcast: true):
- Album cover fetching
- Library browsing
- Large paginated requests
```

### 2. Handle Pagination

For large libraries, always use pagination and fetch pages incrementally:
```json
{
  "context": "browsetracks",
  "data": {
    "offset": 0,
    "limit": 100
  }
}
```

### 3. Cache Aggressively

- Use the `hash` field in cover requests to avoid re-downloading unchanged covers
- Status `304` indicates the cached version is still valid

### 4. Implement Keepalive

The server sends periodic `ping` frames; reply promptly with `pong` (empty-string data) to
keep the connection alive and detect drops.

### 5. Use `init` for Startup State

After the handshake, send `init` to receive the bundled current state rather than requesting
each field individually. The plugin pushes track / rating / lfm-rating / status and
broadcasts cover / lyrics in response.

### 6. Process Broadcasts Efficiently

Broadcasts arrive frequently during playback. Debounce UI updates to avoid performance issues.

---

## Error Handling

### Error Response
```json
{
  "context": "error",
  "data": "Error message"
}
```

### Not Allowed Response
```json
{
  "context": "notallowed",
  "data": null
}
```

Sent when an operation is not permitted (e.g., unauthenticated client).

---

## Discovery

MusicBee Remote supports UDP multicast for service discovery on the local network.

- Multicast Address: `239.1.5.10`
- Port: `45345`

Clients can listen for discovery broadcasts to find available MusicBee instances without
manual configuration. Discovery is a separate UDP path, independent of the TCP command socket.
