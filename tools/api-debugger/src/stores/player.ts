import { computed, ref } from "vue";
import { acceptHMRUpdate, defineStore } from "pinia";
import type { UnlistenFn } from "@tauri-apps/api/event";
import { onMessage, onState, sendCommand } from "../lib/api";
import { emptyNowPlaying, foldResponse, type NowPlaying } from "../lib/player";

/**
 * True when a `nowplayingcover` frame is the bare `{status:1}` readiness marker
 * (art available but not inlined) rather than an actual image payload. The
 * status may arrive as a number or a string.
 */
function isCoverReadyMarker(data: unknown): boolean {
  if (!data || typeof data !== "object") return false;
  const d = data as Record<string, unknown>;
  const hasImage = typeof d.cover === "string" && d.cover.length > 0;
  return !hasImage && String(d.status ?? "") === "1";
}

/** Local seek-bar tick (ms). Advances position client-side between syncs. */
const TICK_MS = 1000;
/** Re-sync position with the plugin every Nth tick (so 15s), to correct drift. */
const SYNC_EVERY = 15;

/**
 * Player-pane store. Mirrors the Direct panel's PRIMARY connection: it adds its
 * own listeners to the primary channel (Tauri fans events out to every
 * listener, so this doesn't disturb the Direct store) and folds the incoming
 * `nowplaying*` / `player*` responses into one reactive NowPlaying object.
 *
 * On connect it fires the same startup batch the Android app sends (init +
 * position + cover) and, while playing, polls position so the seek bar moves.
 * The transport controls send commands back over the primary channel.
 */
export const usePlayerStore = defineStore("player", () => {
  const np = ref<NowPlaying>(emptyNowPlaying());
  const connected = ref(false);
  const showLyrics = ref(false);

  const hasTrack = computed(() => np.value.title !== "" || np.value.artist !== "");

  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let ticks = 0;
  // The backend reports `connected` as soon as the socket opens, before the
  // handshake finishes. Commands are only safe after the plugin's `protocol`
  // reply (the real client sends `init` at exactly that point), so we defer the
  // startup batch until we see it.
  let awaitingHandshake = false;

  function stopPolling() {
    if (pollTimer !== null) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  }

  function startPolling() {
    stopPolling();
    ticks = 0;
    pollTimer = setInterval(() => {
      if (!connected.value || np.value.state !== "Playing") return;
      // Advance the bar locally each second for smooth motion...
      if (np.value.durationMs > 0) {
        np.value.positionMs = Math.min(np.value.positionMs + TICK_MS, np.value.durationMs);
      }
      // ...and resync with the plugin every SYNC_EVERY ticks to correct drift.
      if (++ticks % SYNC_EVERY === 0) void getPosition();
    }, TICK_MS);
  }

  /** Send a command over the primary channel (no-op when disconnected). */
  async function send(context: string, data: unknown) {
    if (!connected.value) return;
    try {
      await sendCommand("primary", JSON.stringify({ context, data }));
    } catch {
      /* surfaced in the Direct log via the backend error emit */
    }
  }

  // ── startup + polling requests ──────────────────────────────────────────────
  const getPosition = () => send("nowplayingposition", ""); // empty data => query, not seek
  async function loadState() {
    await send("init", ""); // track, rating, lfm, playerstatus, cover, lyrics
    await getPosition();
    await send("nowplayingcover", ""); // full artwork (init's cover may be status-only)
  }

  // ── transport controls (payloads mirror the Android client) ─────────────────
  const playPause = () => send("playerplaypause", "");
  const next = () => send("playernext", true);
  const previous = () => send("playerprevious", true);
  const toggleShuffle = () => send("playershuffle", "toggle");
  const toggleRepeat = () => send("playerrepeat", "toggle");
  const toggleMute = () => send("playermute", "toggle");
  const toggleLove = () => send("nowplayinglfmrating", "toggle");
  const setVolume = (v: number) => send("playervolume", Math.max(0, Math.min(100, Math.round(v))));
  const seek = (ms: number) => send("nowplayingposition", Math.max(0, Math.round(ms)));
  const fetchLyrics = () => send("nowplayinglyrics", "");

  function toggleLyrics() {
    showLyrics.value = !showLyrics.value;
    if (showLyrics.value && !np.value.lyrics) void fetchLyrics();
  }

  // ── fold an incoming primary-channel response into state ────────────────────
  function apply(context: string, raw: string) {
    let data: unknown;
    try {
      data = (JSON.parse(raw) as { data?: unknown }).data;
    } catch {
      return; // non-JSON frame
    }
    const patch = foldResponse(context, data);
    if (Object.keys(patch).length > 0) Object.assign(np.value, patch);

    // Cover-ready marker: the plugin broadcasts `nowplayingcover` with
    // `{status:1}` to say new artwork is available but not inlined (e.g. after
    // MusicBee finishes downloading art on a track change). Mirror the real
    // clients and pull the image on a fresh request so it updates without a
    // manual fetch. An explicit `nowplayingcover` request returns the image with
    // status 200/404, never 1, so this cannot loop.
    if (context === "nowplayingcover" && isCoverReadyMarker(data)) {
      void send("nowplayingcover", "");
    }
  }

  function reset() {
    np.value = emptyNowPlaying();
    showLyrics.value = false;
  }

  // ── one-time subscription to the primary channel ────────────────────────────
  let subscribed = false;
  const unlisten: UnlistenFn[] = [];

  async function init() {
    if (subscribed) return;
    subscribed = true;
    unlisten.push(
      await onMessage("primary", (m) => {
        // Only react to responses from the plugin, never our own echoed sends.
        if (m.direction !== "received") return;
        // The plugin's `protocol` reply marks the handshake done → fetch state.
        if (m.context === "protocol" && awaitingHandshake) {
          awaitingHandshake = false;
          void loadState();
        }
        apply(m.context, m.raw);
      }),
    );
    unlisten.push(
      await onState("primary", (s) => {
        const was = connected.value;
        connected.value = s.connected;
        if (s.connected && !was) {
          reset();
          awaitingHandshake = true;
          startPolling();
        } else if (!s.connected && was) {
          awaitingHandshake = false;
          stopPolling();
        }
      }),
    );
  }

  function dispose() {
    stopPolling();
    unlisten.forEach((u) => u());
    unlisten.length = 0;
    subscribed = false;
  }

  return {
    np,
    connected,
    showLyrics,
    hasTrack,
    loadState,
    playPause,
    next,
    previous,
    toggleShuffle,
    toggleRepeat,
    toggleMute,
    toggleLove,
    setVolume,
    seek,
    fetchLyrics,
    toggleLyrics,
    init,
    dispose,
  };
});

if (import.meta.hot) {
  import.meta.hot.accept(acceptHMRUpdate(usePlayerStore, import.meta.hot));
  import.meta.hot.dispose(() => {
    try {
      usePlayerStore().dispose();
    } catch {
      /* store not instantiated in this module context */
    }
  });
}
