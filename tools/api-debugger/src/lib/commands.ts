// Command catalog for the Direct panel.
//
// Contexts mirror core/Protocol/Messages/ProtocolConstants.cs. Each entry has a
// starting `template` and a v4 `data` payload `hint` (from docs/protocol.md).
// Usage flags help a tester know what's relevant:
//   android  - sent by an Android client across the V4 line (Protocol.kt union
//              v1.1.0-v1.6.1, verified against upstream tags + golden c2s)
//   ios      - sent by the iOS app (iOS protocol sheet + golden captures)

export interface CommandDef {
  context: string;
  label: string;
  template: string;
  hint: string;
  android?: boolean;
  ios?: boolean;
}

export interface CommandGroup {
  name: string;
  commands: CommandDef[];
}

interface Opts {
  data?: unknown;
  hint: string;
  android?: boolean;
  ios?: boolean;
}

function cmd(context: string, label: string, o: Opts): CommandDef {
  return {
    context,
    label,
    template: JSON.stringify({ context, data: o.data ?? null }),
    hint: o.hint,
    android: o.android,
    ios: o.ios,
  };
}

export const COMMAND_CATALOG: CommandGroup[] = [
  {
    name: "Playback",
    commands: [
      cmd("playerplaypause", "Play / Pause toggle", { hint: "null", android: true, ios: true }),
      cmd("playerplay", "Play", { hint: "null", android: true }),
      cmd("playerpause", "Pause", { hint: "null", android: true }),
      cmd("playerstop", "Stop", { hint: "null", android: true }),
      cmd("playernext", "Next track", { hint: "null", android: true, ios: true }),
      cmd("playerprevious", "Previous track", { hint: "null", android: true, ios: true }),
      cmd("playerstate", "Player state", { hint: "null", android: true }),
      cmd("playerstatus", "Full player status", { hint: "null", android: true }),
      cmd("playervolume", "Volume", { hint: "null to query · number 0–100 to set", android: true, ios: true }),
      cmd("playermute", "Mute", { hint: 'null · true/false · "toggle"', android: true }),
      cmd("playershuffle", "Shuffle", { hint: 'null · "toggle" · "off"|"shuffle"|"autodj" (V4)', android: true, ios: true }),
      cmd("playerrepeat", "Repeat", { hint: 'null · "toggle" · "None"|"All"|"One"', android: true, ios: true }),
      cmd("playerautodj", "Auto-DJ", { hint: '"toggle" (not used by current clients)' }),
      cmd("scrobbler", "Scrobbling", { hint: 'null · "toggle"', android: true }),
      cmd("playeroutput", "Audio output (V4)", { hint: 'null to query · "DeviceName" to set', android: true }),
      cmd("playeroutputswitch", "Switch output (V4)", { data: "Headphones", hint: '"DeviceName"', android: true }),
    ],
  },
  {
    name: "Now Playing",
    commands: [
      cmd("nowplayingtrack", "Current track", { hint: "null", android: true }),
      cmd("nowplayingdetails", "Track details (V4)", { hint: "null", android: true, ios: true }),
      cmd("nowplayingcover", "Cover art", { hint: "null", android: true, ios: true }),
      cmd("nowplayinglyrics", "Lyrics", { hint: "null", android: true, ios: true }),
      cmd("nowplayingposition", "Position", { hint: "null to query · number ms to seek", android: true, ios: true }),
      cmd("nowplayingrating", "Rating", { hint: 'null · "0"–"5" to set · "" clears', android: true, ios: true }),
      cmd("nowplayinglfmrating", "Last.fm love/ban", { hint: 'null · "toggle" · "love" · "ban"', android: true, ios: true }),
      cmd("nowplayinglist", "Now-playing list", {
        data: { offset: 0, limit: 100 },
        hint: "{ offset, limit } (V3+) · null for all (V2)",
        android: true,
        ios: true,
      }),
      cmd("nowplayinglistplay", "Play list item", { data: 0, hint: "number - track index", android: true, ios: true }),
      cmd("nowplayinglistremove", "Remove list item", { data: { index: 0 }, hint: "number index · or { index } (V3+)", android: true, ios: true }),
      cmd("nowplayinglistmove", "Move list item", { data: { from: 0, to: 0 }, hint: "{ from, to }", android: true, ios: true }),
      cmd("nowplayinglistsearch", "Search list", { data: "query", hint: "string · Android ≤1.5.1 (removed in 1.6.0)", android: true }),
      cmd("nowplayingqueue", "Queue files", {
        data: { queue: "next", play: null, data: ["file:///path/to/song.mp3"] },
        hint: '{ queue: "next"|"last"|"now", play, data: [paths] }',
        android: true,
        ios: true,
      }),
      cmd("nowplayingtagchange", "Change tag (V4)", {
        data: { tag: "artist", value: "New Value" },
        hint: "{ tag, value }",
        ios: true,
      }),
    ],
  },
  {
    name: "Library",
    commands: [
      cmd("browseartists", "Browse artists", {
        data: { offset: 0, limit: 100, album_artists: false },
        hint: "{ offset, limit, album_artists }",
        android: true,
        ios: true,
      }),
      cmd("browsealbums", "Browse albums", { data: { offset: 0, limit: 100 }, hint: "{ offset, limit }", android: true, ios: true }),
      cmd("browsegenres", "Browse genres", { data: { offset: 0, limit: 100 }, hint: "{ offset, limit }", android: true, ios: true }),
      cmd("browsetracks", "Browse tracks", { data: { offset: 0, limit: 100 }, hint: "{ offset, limit }", android: true, ios: true }),
      cmd("libraryalbumcover", "Album cover (V4)", {
        data: { artist: "Artist", album: "Album", offset: 0, limit: 20 },
        hint: "{ artist, album, offset, limit }",
        android: true,
        ios: true,
      }),
      cmd("libraryplayall", "Play all (V4)", { data: false, hint: "bool - shuffle", android: true, ios: true }),
      cmd("libraryartistalbums", "Albums for artist", { data: "Artist Name", hint: "string - artist", ios: true }),
      cmd("libraryalbumtracks", "Tracks for album", { data: "Album Name", hint: "string - album", ios: true }),
      cmd("librarygenreartists", "Artists for genre", { data: "Genre Name", hint: "string - genre", ios: true }),
      cmd("librarycovercachebuildstatus", "Cover-cache status (V4)", { hint: "null", ios: true }),
      cmd("librarysearchtitle", "Search titles", { data: "query", hint: "string (not used by current clients)" }),
      cmd("librarysearchartist", "Search artists", { data: "query", hint: "string (not used by current clients)" }),
      cmd("librarysearchalbum", "Search albums", { data: "query", hint: "string (not used by current clients)" }),
      cmd("librarysearchgenre", "Search genres", { data: "query", hint: "string (not used by current clients)" }),
      cmd("libraryqueuetrack", "Queue track", { data: { query: "Song Title" }, hint: "{ query } (not used by current clients)" }),
      cmd("libraryqueuealbum", "Queue album", { data: { query: "Album" }, hint: "{ query } (not used by current clients)" }),
      cmd("libraryqueueartist", "Queue artist", { data: { query: "Artist" }, hint: "{ query } (not used by current clients)" }),
      cmd("libraryqueuegenre", "Queue genre", { data: { query: "Genre" }, hint: "{ query } (not used by current clients)" }),
    ],
  },
  {
    name: "Playlists",
    commands: [
      cmd("playlistlist", "List playlists", { data: { offset: 0, limit: 100 }, hint: "{ offset, limit } · null for all", android: true, ios: true }),
      cmd("playlistplay", "Play playlist", { data: "playlist_url", hint: "string - playlist url/path", android: true, ios: true }),
      cmd("radiostations", "Radio stations (V4)", { data: { offset: 0, limit: 50 }, hint: "{ offset, limit }", android: true }),
    ],
  },
  {
    name: "Connection",
    commands: [
      cmd("verifyconnection", "Verify connection (V4)", { hint: "null", android: true, ios: true }),
      cmd("pluginversion", "Plugin version", { hint: "null", android: true, ios: true }),
      cmd("init", "Initial state sync", { hint: "null - triggers multiple responses", android: true, ios: true }),
      // Real traffic sends ping/pong data as "" (empty string), not null (see docs note).
      cmd("ping", "Ping", { data: "", hint: '"" (empty string)', android: true, ios: true }),
      cmd("player", "Handshake: client type", { data: "Android", hint: 'string - "Android" | "iOS" | …', android: true, ios: true }),
      cmd("protocol", "Handshake: protocol", {
        data: { protocol_version: 4, no_broadcast: false },
        hint: "{ protocol_version, no_broadcast, client_id? }",
        android: true,
        ios: true,
      }),
    ],
  },
];

/** Find the catalog entry for a composed message's context, if any. */
export function findCommand(json: string): CommandDef | undefined {
  let context: unknown;
  try {
    context = (JSON.parse(json) as { context?: unknown }).context;
  } catch {
    return undefined;
  }
  if (typeof context !== "string") return undefined;
  for (const g of COMMAND_CATALOG) {
    const found = g.commands.find((c) => c.context === context);
    if (found) return found;
  }
  return undefined;
}
