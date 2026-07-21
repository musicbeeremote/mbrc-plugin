/**
 * Protocol adapters for the Player pane.
 *
 * The pane and its store are protocol-agnostic: they call semantic actions
 * (playPause, setVolume, seek, ...) and feed every inbound frame to `fold`,
 * without knowing whether the primary socket speaks legacy V4 or V6. Each
 * adapter translates those actions into wire frames and folds inbound frames
 * (responses AND broadcast events) into a `NowPlaying` patch.
 *
 * Key protocol differences the adapters hide:
 *  - Framing: V4 is `{context,data}`; V6 is an envelope `{id,kind,op|event,data}`.
 *  - Toggles: V4 has server-side `"toggle"` payloads; V6 has only explicit
 *    setters, so the adapter computes the next value from current state.
 *  - Correlation: a V6 `response` carries no op name, only the `id` it answers,
 *    so the V6 adapter remembers which op each id was, to fold the reply.
 *  - Handshake-ready: V4 waits for the plugin's `protocol` reply; V6's handshake
 *    is automatic and done when its `id:0` response returns.
 */

import type { WireMessage } from "./api";
import {
  clampVolume,
  foldResponse,
  imageMime,
  looksBase64,
  parseLfm,
  parseRepeat,
  parseShuffle,
  parseState,
  str,
  type LfmStatus,
  type NowPlaying,
  type RepeatMode,
  type ShuffleMode,
} from "./player";

/** Result of folding one inbound frame. */
export interface FoldOutcome {
  /** State patch to merge (may be empty). */
  patch: Partial<NowPlaying>;
  /** Frames to send as a consequence (e.g. cover-ready -> fetch the image). */
  followUps: string[];
  /** The now-playing lyrics went stale (track changed) - refetch if shown. */
  lyricsStale?: boolean;
}

const EMPTY: FoldOutcome = { patch: {}, followUps: [] };

/**
 * A protocol-specific translator. All methods return ready-to-send wire frames
 * (JSON strings); the store just pushes them over the primary socket.
 */
export interface PlayerAdapter {
  readonly protocol: number;
  /** The read burst to warm state once the handshake is ready. */
  startup(): string[];
  /** A position query (drives the seek-bar drift resync). */
  positionPoll(): string;
  /** Fetch the structured lyrics for the current track. */
  fetchLyrics(): string;
  playPause(): string;
  next(): string;
  previous(): string;
  toggleShuffle(np: NowPlaying): string;
  toggleRepeat(np: NowPlaying): string;
  toggleMute(np: NowPlaying): string;
  toggleLove(np: NowPlaying): string;
  setVolume(v: number): string;
  seek(ms: number): string;
  /** True when this inbound frame signals the handshake is complete. */
  isReady(m: WireMessage): boolean;
  /** Fold an inbound frame into a patch (+ any follow-up frames). */
  fold(m: WireMessage): FoldOutcome;
}

/** Parse a frame's `data` field, tolerating non-JSON lines. */
function frameData(raw: string): unknown {
  try {
    return (JSON.parse(raw) as { data?: unknown }).data;
  } catch {
    return undefined;
  }
}

/**
 * True when a `nowplayingcover` frame is the bare `{status:1}` readiness marker
 * (art available but not inlined) rather than an actual image payload.
 */
function isCoverReadyMarker(data: unknown): boolean {
  if (!data || typeof data !== "object") return false;
  const d = data as Record<string, unknown>;
  const hasImage = typeof d.cover === "string" && d.cover.length > 0;
  return !hasImage && String(d.status ?? "") === "1";
}

// ── V4 (legacy {context,data}) ───────────────────────────────────────────────

function v4Adapter(): PlayerAdapter {
  const frame = (context: string, data: unknown) => JSON.stringify({ context, data });
  return {
    protocol: 4,
    // init bundles track/rating/lfm/playerstatus/cover/lyrics; then position and
    // a full cover (init's cover may be status-only).
    startup: () => [frame("init", ""), frame("nowplayingposition", ""), frame("nowplayingcover", "")],
    positionPoll: () => frame("nowplayingposition", ""),
    fetchLyrics: () => frame("nowplayinglyrics", ""),
    playPause: () => frame("playerplaypause", ""),
    next: () => frame("playernext", true),
    previous: () => frame("playerprevious", true),
    // Server-side toggles: current state is irrelevant.
    toggleShuffle: () => frame("playershuffle", "toggle"),
    toggleRepeat: () => frame("playerrepeat", "toggle"),
    toggleMute: () => frame("playermute", "toggle"),
    toggleLove: () => frame("nowplayinglfmrating", "toggle"),
    setVolume: (v) => frame("playervolume", clampVolume(v)),
    seek: (ms) => frame("nowplayingposition", Math.max(0, Math.round(ms))),
    isReady: (m) => m.direction === "received" && m.context === "protocol",
    fold: (m) => {
      if (m.direction !== "received") return EMPTY;
      const data = frameData(m.raw);
      const patch = foldResponse(m.context, data);
      // Cover-ready marker -> pull the actual image (never loops: an explicit
      // request returns status 200/404, not 1).
      const followUps =
        m.context === "nowplayingcover" && isCoverReadyMarker(data)
          ? [frame("nowplayingcover", "")]
          : [];
      return { patch, followUps };
    },
  };
}

// ── V6 (envelope {id,kind,op|event,data}) ────────────────────────────────────

/**
 * Ops whose response we fold. Read ops carry the state; setters echo their new
 * canonical value, which we MUST fold too - shuffle/repeat/lfm have no broadcast
 * event, so the reply is the only signal the UI gets. (volume/mute also emit an
 * event, but folding the reply is harmless and more immediate.)
 */
const V6_FOLDED_OPS = new Set([
  "player_status",
  "now_playing_state",
  "now_playing_position",
  "now_playing_lyrics",
  "cover_get",
  "player_set_shuffle",
  "player_set_repeat",
  "player_set_mute",
  "player_set_volume",
  "now_playing_set_lfm",
]);

function nextShuffle(mode: ShuffleMode): string {
  // Only flip the on/off axis; leave autodj out of the manual toggle.
  return mode === "off" ? "shuffle" : "off";
}

function nextRepeat(mode: RepeatMode): string {
  if (mode === "None" || mode === "Undefined") return "all";
  if (mode === "All") return "one";
  return "none";
}

function nextLove(lfm: LfmStatus): string {
  return lfm === "Love" ? "normal" : "love";
}

/** Fold the canonical V6 track object into title/artist/album/year. */
function foldTrack(track: unknown): Partial<NowPlaying> {
  if (!track || typeof track !== "object") return { title: "", artist: "", album: "", year: "" };
  const t = track as Record<string, unknown>;
  return {
    title: str(t.title),
    artist: str(t.artist),
    album: str(t.album),
    year: t.year == null ? "" : str(t.year),
  };
}

/** Fold V6 structured lyrics `{type, lines:[{text, at_ms?}]}` into flat text. */
function foldV6Lyrics(data: unknown): Partial<NowPlaying> {
  if (!data || typeof data !== "object") return { lyrics: "", lyricsStatus: "no lyrics available" };
  const d = data as { type?: unknown; lines?: unknown };
  const kind = str(d.type);
  if (kind === "none" || !Array.isArray(d.lines) || d.lines.length === 0) {
    return { lyrics: "", lyricsStatus: "no lyrics available" };
  }
  const text = d.lines
    .map((l) => (l && typeof l === "object" ? str((l as Record<string, unknown>).text) : ""))
    .join("\n");
  return { lyrics: text, lyricsStatus: kind === "synced" ? "synced" : "plain" };
}

function v6Adapter(): PlayerAdapter {
  // A dedicated id space (well above the Direct panel's 1+ sequence) so this
  // pane's requests never collide with manual sends on the shared socket.
  let nextId = 1_000_000;
  const pending = new Map<number, string>(); // id -> op, for response correlation
  let lastCoverHash = "";

  function req(op: string, data: unknown): string {
    const id = nextId++;
    if (V6_FOLDED_OPS.has(op)) pending.set(id, op);
    return JSON.stringify({ id, kind: "request", op, data });
  }

  const state = () => req("now_playing_state", {});

  return {
    protocol: 6,
    startup: () => [req("player_status", {}), state(), req("now_playing_position", {})],
    positionPoll: () => req("now_playing_position", {}),
    fetchLyrics: () => req("now_playing_lyrics", {}),
    playPause: () => req("player_play_pause", {}),
    next: () => req("player_next", {}),
    previous: () => req("player_previous", {}),
    toggleShuffle: (np) => req("player_set_shuffle", { mode: nextShuffle(np.shuffle) }),
    toggleRepeat: (np) => req("player_set_repeat", { mode: nextRepeat(np.repeat) }),
    toggleMute: (np) => req("player_set_mute", { muted: !np.muted }),
    toggleLove: (np) => req("now_playing_set_lfm", { status: nextLove(np.lfm) }),
    setVolume: (v) => req("player_set_volume", { volume: clampVolume(v) }),
    seek: (ms) => req("now_playing_seek", { position_ms: Math.max(0, Math.round(ms)) }),
    // The handshake is auto-sent by the backend; its id:0 response means ready.
    isReady: (m) => m.direction === "received" && m.kind === "response" && m.id === 0,
    fold: (m) => {
      if (m.direction !== "received") return EMPTY;
      const data = frameData(m.raw);
      const d = (data && typeof data === "object" ? data : {}) as Record<string, unknown>;

      // Broadcast events are self-describing (carry `event`).
      if (m.event) {
        switch (m.event) {
          case "play_state_changed":
            return { patch: { state: parseState(d.play_state) }, followUps: [] };
          case "volume_changed":
            return { patch: { volume: clampVolume(d.volume) }, followUps: [] };
          case "mute_changed":
            return { patch: { muted: Boolean(d.muted) }, followUps: [] };
          case "now_playing_changed":
            // Immediate title/artist/album from the event; re-query state for
            // year/position/lfm/cover_hash.
            return { patch: foldTrack(d), followUps: [state()], lyricsStale: true };
          case "now_playing_lyrics_changed":
            return { patch: {}, followUps: [], lyricsStale: true };
          case "cover_cache_changed":
            return { patch: {}, followUps: [state()] };
          default:
            return EMPTY;
        }
      }

      // Responses carry no op name - correlate by id.
      if (m.kind === "response" && typeof m.id === "number") {
        const op = pending.get(m.id);
        if (op === undefined) return EMPTY;
        pending.delete(m.id);
        switch (op) {
          case "player_status":
            return {
              patch: {
                state: parseState(d.play_state),
                volume: clampVolume(d.volume),
                muted: Boolean(d.muted),
                shuffle: parseShuffle(d.shuffle),
                repeat: parseRepeat(d.repeat),
              },
              followUps: [],
            };
          case "now_playing_position":
            return {
              patch: {
                positionMs: typeof d.position_ms === "number" ? d.position_ms : 0,
                durationMs: typeof d.duration_ms === "number" ? d.duration_ms : 0,
              },
              followUps: [],
            };
          case "now_playing_state": {
            const patch: Partial<NowPlaying> = {
              ...foldTrack(d.track),
              lfm: parseLfm(d.lfm_status),
            };
            if (typeof d.position_ms === "number") patch.positionMs = d.position_ms;
            if (typeof d.duration_ms === "number") patch.durationMs = d.duration_ms;
            // Fetch cover only when the hash changed (avoids refetch churn).
            const hash =
              d.track && typeof d.track === "object"
                ? str((d.track as Record<string, unknown>).cover_hash)
                : "";
            const followUps: string[] = [];
            if (hash && hash !== lastCoverHash) {
              lastCoverHash = hash;
              followUps.push(req("cover_get", { hash }));
            } else if (!hash) {
              patch.coverDataUrl = null;
              patch.coverStatus = "no cover available";
              lastCoverHash = "";
            }
            return { patch, followUps };
          }
          case "now_playing_lyrics":
            return { patch: foldV6Lyrics(data), followUps: [] };
          // Setter replies echo the new canonical value (field names differ from
          // the pane's model: shuffle/repeat come back under `mode`).
          case "player_set_shuffle":
            return { patch: { shuffle: parseShuffle(d.mode) }, followUps: [] };
          case "player_set_repeat":
            return { patch: { repeat: parseRepeat(d.mode) }, followUps: [] };
          case "player_set_mute":
            return { patch: { muted: Boolean(d.muted) }, followUps: [] };
          case "player_set_volume":
            return { patch: { volume: clampVolume(d.volume) }, followUps: [] };
          case "now_playing_set_lfm":
            return { patch: { lfm: parseLfm(d.lfm_status) }, followUps: [] };
          case "cover_get": {
            if (d.not_modified === true) return { patch: { coverStatus: "cover unchanged" }, followUps: [] };
            const image = typeof d.image === "string" ? d.image : "";
            if (image && looksBase64(image)) {
              return {
                patch: {
                  coverDataUrl: `data:${imageMime(image)};base64,${image}`,
                  coverStatus: "cover received",
                },
                followUps: [],
              };
            }
            return EMPTY;
          }
          default:
            return EMPTY;
        }
      }
      // Ignore our own echoed requests and error frames here (surfaced elsewhere).
      return EMPTY;
    },
  };
}

/** Build the adapter for a protocol version (4 = legacy, 6 = V6). */
export function createPlayerAdapter(protocol: number): PlayerAdapter {
  return protocol === 6 ? v6Adapter() : v4Adapter();
}
