/**
 * Now-Playing state model + pure response folding for the Player pane.
 *
 * The plugin doesn't send one "now playing" object - state arrives piecemeal as
 * separate `nowplaying*` / `player*` responses (and broadcasts). `foldResponse`
 * turns one such response into a partial patch; the store merges patches into a
 * single reactive `NowPlaying`. Kept pure (no Vue, no I/O) so it's unit-tested.
 */

export type PlayState = "Playing" | "Paused" | "Stopped" | "Unknown";
export type ShuffleMode = "off" | "shuffle" | "autodj" | "unknown";
export type RepeatMode = "None" | "All" | "One" | "Undefined";
export type LfmStatus = "Love" | "Normal" | "Ban" | "unknown";

export interface NowPlaying {
  title: string;
  artist: string;
  album: string;
  year: string;
  /** `data:` URL for the decoded cover, or null if none received yet. */
  coverDataUrl: string | null;
  coverStatus: string;
  lyrics: string;
  lyricsStatus: string;
  state: PlayState;
  volume: number; // 0-100
  muted: boolean;
  shuffle: ShuffleMode;
  repeat: RepeatMode;
  positionMs: number;
  durationMs: number;
  lfm: LfmStatus;
}

export function emptyNowPlaying(): NowPlaying {
  return {
    title: "",
    artist: "",
    album: "",
    year: "",
    coverDataUrl: null,
    coverStatus: "",
    lyrics: "",
    lyricsStatus: "",
    state: "Unknown",
    volume: 0,
    muted: false,
    shuffle: "unknown",
    repeat: "Undefined",
    positionMs: 0,
    durationMs: 0,
    lfm: "unknown",
  };
}

function str(v: unknown): string {
  return v == null ? "" : String(v);
}

function clampVolume(v: unknown): number {
  const n = Number(v);
  if (!Number.isFinite(n)) return 0;
  return Math.max(0, Math.min(100, Math.round(n)));
}

function parseState(v: unknown): PlayState {
  const s = str(v).toLowerCase();
  if (s === "playing") return "Playing";
  if (s === "paused") return "Paused";
  if (s === "stopped") return "Stopped";
  return "Unknown";
}

function parseShuffle(v: unknown): ShuffleMode {
  if (typeof v === "boolean") return v ? "shuffle" : "off";
  const s = str(v).toLowerCase();
  if (s === "off") return "off";
  if (s === "shuffle") return "shuffle";
  if (s === "autodj") return "autodj";
  return "unknown";
}

function parseRepeat(v: unknown): RepeatMode {
  const s = str(v).toLowerCase();
  if (s === "none") return "None";
  if (s === "all") return "All";
  if (s === "one") return "One";
  return "Undefined";
}

function parseLfm(v: unknown): LfmStatus {
  const s = str(v).toLowerCase();
  if (s === "love") return "Love";
  if (s === "ban") return "Ban";
  if (s === "normal") return "Normal";
  return "unknown";
}

/** Sniff the image type from a base64 payload's leading bytes. */
function imageMime(base64: string): string {
  if (base64.startsWith("/9j/")) return "image/jpeg";
  if (base64.startsWith("iVBOR")) return "image/png";
  if (base64.startsWith("R0lGOD")) return "image/gif";
  if (base64.startsWith("UklGR")) return "image/webp";
  return "image/png";
}

/** True for a non-empty, plausibly-base64 string (not a status token). */
function looksBase64(v: string): boolean {
  return v.length > 16 && /^[A-Za-z0-9+/]+={0,2}$/.test(v);
}

function foldCover(data: unknown): Partial<NowPlaying> {
  // String form: raw base64 (older protocol).
  if (typeof data === "string") {
    return looksBase64(data)
      ? { coverDataUrl: `data:${imageMime(data)};base64,${data}`, coverStatus: "cover received" }
      : { coverStatus: `unexpected cover string (len ${data.length})` };
  }
  if (data && typeof data === "object") {
    const d = data as Record<string, unknown>;
    const cover = typeof d.cover === "string" ? d.cover : "";
    if (cover && looksBase64(cover)) {
      return { coverDataUrl: `data:${imageMime(cover)};base64,${cover}`, coverStatus: "cover received" };
    }
    // Status-only replies (1 = ready but not included, 404 = none). Keep any
    // existing art; only update the status line.
    const status = str(d.status);
    if (status === "1") return { coverStatus: "cover ready (not included)" };
    if (status === "404") return { coverStatus: "no cover available" };
    return { coverStatus: `cover status: ${status || "unknown"}` };
  }
  return {};
}

function foldLyrics(data: unknown): Partial<NowPlaying> {
  let text = "";
  if (typeof data === "string") {
    text = data;
  } else if (data && typeof data === "object") {
    const d = data as Record<string, unknown>;
    if (str(d.status) === "404") return { lyrics: "", lyricsStatus: "no lyrics available" };
    text = str(d.lyrics ?? d.text);
  }
  return text.trim()
    ? { lyrics: text, lyricsStatus: `${text.length} chars` }
    : { lyrics: "", lyricsStatus: "no lyrics available" };
}

/**
 * Fold one response (`context` + already-parsed `data`) into a NowPlaying patch.
 * Unknown contexts and malformed data yield an empty patch (no-op).
 */
export function foldResponse(context: string, data: unknown): Partial<NowPlaying> {
  switch (context) {
    case "nowplayingtrack": {
      if (!data || typeof data !== "object") return {};
      const d = data as Record<string, unknown>;
      return { title: str(d.title), artist: str(d.artist), album: str(d.album), year: str(d.year) };
    }
    case "playerstatus": {
      if (!data || typeof data !== "object") return {};
      const d = data as Record<string, unknown>;
      const patch: Partial<NowPlaying> = {};
      if (d.playerstate !== undefined) patch.state = parseState(d.playerstate);
      if (d.playervolume !== undefined) patch.volume = clampVolume(d.playervolume);
      if (d.playermute !== undefined) patch.muted = Boolean(d.playermute);
      if (d.playershuffle !== undefined) patch.shuffle = parseShuffle(d.playershuffle);
      if (d.playerrepeat !== undefined) patch.repeat = parseRepeat(d.playerrepeat);
      return patch;
    }
    case "playerstate":
      return { state: parseState(data) };
    case "playervolume":
      return { volume: clampVolume(data) };
    case "playermute":
      return typeof data === "boolean" ? { muted: data } : {};
    case "playershuffle":
      return { shuffle: parseShuffle(data) };
    case "playerrepeat":
      return { repeat: parseRepeat(data) };
    case "nowplayingposition": {
      if (!data || typeof data !== "object") return {};
      const d = data as Record<string, unknown>;
      const patch: Partial<NowPlaying> = {};
      if (typeof d.current === "number") patch.positionMs = d.current;
      if (typeof d.total === "number") patch.durationMs = d.total;
      return patch;
    }
    case "nowplayingcover":
    case "libraryalbumcover":
      return foldCover(data);
    case "nowplayinglyrics":
      return foldLyrics(data);
    case "nowplayinglfmrating":
      return { lfm: parseLfm(data) };
    default:
      return {};
  }
}

/** Format milliseconds as `m:ss` (or `h:mm:ss` past an hour). */
export function formatMs(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) return "0:00";
  const total = Math.floor(ms / 1000);
  const s = total % 60;
  const m = Math.floor(total / 60) % 60;
  const h = Math.floor(total / 3600);
  const ss = String(s).padStart(2, "0");
  if (h > 0) return `${h}:${String(m).padStart(2, "0")}:${ss}`;
  return `${m}:${ss}`;
}

export function shuffleActive(mode: ShuffleMode): boolean {
  return mode === "shuffle" || mode === "autodj";
}
