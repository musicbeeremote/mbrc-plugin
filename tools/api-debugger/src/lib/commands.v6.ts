// V6 op catalog for the Direct panel (selected when Protocol = 6).
//
// V6 is the strict clean-slate protocol (MBRCIP-0003 / issue #118): a JSON
// envelope `{id, kind:"request", op, data}`, newline-framed, with typed
// `kind:"response"` replies. Unlike the legacy `{context,data}` catalog, entries
// here are ops; the composer template carries `kind`/`op`/`data`, and the store
// injects a per-connection `id` at send time.
//
// The `handshake` op is sent automatically by the backend on connect (a second one
// is a protocol error), so it is not a manual entry. On a fresh V6 connection the
// store also replays `V6_INIT_OPS` (see below) to mirror a real client's warm-up.

export interface V6CommandDef {
  op: string;
  label: string;
  /** Envelope template WITHOUT `id` - the store injects the correlation id. */
  template: string;
  hint: string;
}

export interface V6CommandGroup {
  name: string;
  commands: V6CommandDef[];
}

function v6cmd(op: string, label: string, data: unknown, hint: string): V6CommandDef {
  return { op, label, template: JSON.stringify({ kind: "request", op, data }), hint };
}

export const V6_COMMAND_CATALOG: V6CommandGroup[] = [
  {
    name: "Spine",
    commands: [
      v6cmd("ping", "Ping (echo)", {}, "any object - echoed back in the response's data"),
    ],
  },
  {
    name: "System",
    commands: [
      v6cmd("system_info", "System info", {}, "no data · returns { plugin_version, protocol_version }"),
    ],
  },
  {
    name: "Library",
    commands: [
      v6cmd("library_genres", "Browse genres", { offset: 0, limit: 100 }, "{ offset?, limit? }"),
      v6cmd("library_artists", "Browse artists", { offset: 0, limit: 100 }, "{ offset?, limit?, album_artists?, genre? } · genre filters"),
      v6cmd("library_albums", "Browse albums", { offset: 0, limit: 100 }, "{ offset?, limit?, artist? } · artist filters · items carry cover_hash"),
      v6cmd("library_tracks", "Browse tracks", { offset: 0, limit: 100 }, "{ offset?, limit?, album? } · album filters · canonical typed tracks"),
      v6cmd("library_radio", "Radio stations", { offset: 0, limit: 50 }, "{ offset?, limit? }"),
      v6cmd("library_play_all", "Play all", { shuffle: false }, "{ shuffle? }"),
    ],
  },
  {
    name: "Playlist",
    commands: [
      v6cmd("playlist_list", "List playlists", { offset: 0, limit: 100 }, "{ offset?, limit? }"),
      v6cmd("playlist_play", "Play playlist", { url: "" }, "{ url } · playlist path/url"),
    ],
  },
  {
    name: "Track",
    commands: [
      v6cmd("track_get", "Get track by path", { src: "C:\\Music\\song.mp3" }, "{ src } · returns the canonical typed track"),
      v6cmd("cover_get", "Get cover by hash", { hash: "", client_hash: "" }, "{ hash, client_hash? } · base64 image or not_modified"),
    ],
  },
  {
    name: "Now Playing",
    commands: [
      v6cmd("now_playing_state", "State", {}, "current track (canonical) + position + lfm_status"),
      v6cmd("now_playing_details", "Extended details", {}, "publisher/composer/counts/format/..."),
      v6cmd("now_playing_position", "Position", {}, "{ position_ms, duration_ms }"),
      v6cmd("now_playing_lyrics", "Lyrics", {}, "structured { type, lines:[{text, at_ms?}] } (#113)"),
      v6cmd("now_playing_seek", "Seek", { position_ms: 30000 }, "{ position_ms }"),
      v6cmd("now_playing_set_rating", "Set rating", { rating: 4 }, "{ rating: 0-5 | null }"),
      v6cmd("now_playing_set_lfm", "Set last.fm", { status: "love" }, '{ status: "normal"|"love"|"ban" }'),
      v6cmd("now_playing_set_tag", "Edit tag", { tag: "artist", value: "New Value" }, "{ tag, value }"),
      v6cmd("now_playing_list", "Queue (list)", { offset: 0, limit: 100, up_next: false }, "{ offset?, limit?, up_next? } · up_next=true → shuffle play order (drops played); items carry order (mutation key) + position + play_position (-1 = played)"),
      v6cmd("now_playing_list_play", "Play list item", { index: 0 }, "{ index } · 0-based order"),
      v6cmd("now_playing_list_remove", "Remove list item", { index: 0 }, "{ index }"),
      v6cmd("now_playing_list_move", "Move list item", { from: 0, to: 1 }, "{ from, to }"),
      v6cmd("now_playing_list_search", "Search list", { query: "" }, "{ query }"),
      v6cmd("now_playing_queue", "Queue files", { paths: ["file:///path.mp3"], mode: "next" }, '{ paths, mode?: "next"|"last"|"now"|"add-all", play? }'),
    ],
  },
  {
    name: "Player",
    commands: [
      v6cmd("player_play_pause", "Play / Pause", {}, "no data · play_state_changed event follows"),
      v6cmd("player_play", "Play", {}, "no data"),
      v6cmd("player_pause", "Pause", {}, "no data"),
      v6cmd("player_stop", "Stop", {}, "no data"),
      v6cmd("player_next", "Next track", {}, "no data"),
      v6cmd("player_previous", "Previous track", {}, "no data"),
      v6cmd("player_set_volume", "Set volume", { volume: 50 }, "{ volume: 0-100 }"),
      v6cmd("player_set_mute", "Set mute", { muted: true }, "{ muted: bool }"),
      v6cmd("player_set_shuffle", "Set shuffle", { mode: "shuffle" }, '{ mode: "off"|"shuffle"|"autodj" }'),
      v6cmd("player_set_repeat", "Set repeat", { mode: "all" }, '{ mode: "none"|"all"|"one" }'),
      v6cmd("player_set_scrobbling", "Set scrobbling", { enabled: true }, "{ enabled: bool }"),
      v6cmd("player_status", "Player status", {}, "no data · returns the full typed state"),
      v6cmd("player_output", "Output devices", {}, "no data · returns { active, devices }"),
      v6cmd("player_set_output", "Switch output", { device: "Speakers" }, "{ device: string }"),
    ],
  },
];

/**
 * The read ops a real client fires right after the handshake to warm its UI.
 * V4 clients send an `init` burst (`nowplayingtrack`/`nowplayingrating`/
 * `nowplayinglfmrating`/`playerstatus`/`nowplayinglyrics`/`pluginversion`/
 * `nowplayingposition`); V6 collapses those into these ops. All take empty data,
 * so the store can fire them without a per-op template. Order mirrors the trace:
 * plugin/protocol first, then transport state, then the now-playing snapshot.
 */
export const V6_INIT_OPS: readonly string[] = [
  "system_info",
  "player_status",
  "now_playing_state",
  "now_playing_position",
  "now_playing_lyrics",
];

/** Find the V6 catalog entry for a composed envelope's op, if any. */
export function findV6Command(json: string): V6CommandDef | undefined {
  let op: unknown;
  try {
    op = (JSON.parse(json) as { op?: unknown }).op;
  } catch {
    return undefined;
  }
  if (typeof op !== "string") return undefined;
  for (const g of V6_COMMAND_CATALOG) {
    const found = g.commands.find((c) => c.op === op);
    if (found) return found;
  }
  return undefined;
}
