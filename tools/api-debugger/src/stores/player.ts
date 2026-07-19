import { computed, ref } from "vue";
import { acceptHMRUpdate, defineStore } from "pinia";
import type { UnlistenFn } from "@tauri-apps/api/event";
import { onMessage, onState, sendCommand } from "../lib/api";
import { emptyNowPlaying, type NowPlaying } from "../lib/player";
import { createPlayerAdapter, type PlayerAdapter } from "../lib/player-protocol";
import { useDirectStore } from "./direct";

/** Local seek-bar tick (ms). Advances position client-side between syncs. */
const TICK_MS = 1000;
/** Re-sync position with the plugin every Nth tick (so 15s), to correct drift. */
const SYNC_EVERY = 15;

/**
 * Player-pane store. Mirrors the Direct panel's PRIMARY connection: it adds its
 * own listeners to the primary channel (Tauri fans events out to every
 * listener, so this doesn't disturb the Direct store) and folds the incoming
 * responses/events into one reactive NowPlaying object.
 *
 * It is protocol-agnostic: a `PlayerAdapter` (chosen from the primary
 * connection's protocol at connect time) translates every action into wire
 * frames and folds every inbound frame. The store only merges patches, sends
 * the adapter's frames, and runs the seek-bar poll - it never mentions a
 * protocol. On connect it replays the adapter's startup batch once the
 * handshake is ready and, while playing, polls position so the bar moves.
 */
export const usePlayerStore = defineStore("player", () => {
  const np = ref<NowPlaying>(emptyNowPlaying());
  const connected = ref(false);
  const showLyrics = ref(false);

  const hasTrack = computed(() => np.value.title !== "" || np.value.artist !== "");

  // The active protocol translator, rebuilt each connect (protocol may change
  // between connections). Null while disconnected.
  let adapter: PlayerAdapter | null = null;
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let ticks = 0;
  // The backend reports `connected` as soon as the socket opens, before the
  // handshake finishes. Commands are only safe once the adapter reports the
  // handshake ready, so we defer the startup batch until then.
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
      if (++ticks % SYNC_EVERY === 0) void poll();
    }, TICK_MS);
  }

  /** Send a pre-built wire frame over the primary channel (no-op if unwired). */
  async function sendFrame(frame: string | null | undefined) {
    if (!connected.value || !adapter || !frame) return;
    try {
      await sendCommand("primary", frame);
    } catch {
      /* surfaced in the Direct log via the backend error emit */
    }
  }

  // ── startup + polling ───────────────────────────────────────────────────────
  const poll = () => sendFrame(adapter?.positionPoll());
  async function loadState() {
    if (!adapter) return;
    for (const frame of adapter.startup()) await sendFrame(frame);
  }

  // ── transport controls (delegated to the active adapter) ────────────────────
  const playPause = () => sendFrame(adapter?.playPause());
  const next = () => sendFrame(adapter?.next());
  const previous = () => sendFrame(adapter?.previous());
  const toggleShuffle = () => sendFrame(adapter?.toggleShuffle(np.value));
  const toggleRepeat = () => sendFrame(adapter?.toggleRepeat(np.value));
  const toggleMute = () => sendFrame(adapter?.toggleMute(np.value));
  const toggleLove = () => sendFrame(adapter?.toggleLove(np.value));
  const setVolume = (v: number) => sendFrame(adapter?.setVolume(v));
  const seek = (ms: number) => sendFrame(adapter?.seek(ms));
  const fetchLyrics = () => sendFrame(adapter?.fetchLyrics());

  function toggleLyrics() {
    showLyrics.value = !showLyrics.value;
    if (showLyrics.value && !np.value.lyrics) void fetchLyrics();
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
        if (!adapter) return;
        // The handshake-ready frame marks state safe to fetch.
        if (awaitingHandshake && adapter.isReady(m)) {
          awaitingHandshake = false;
          void loadState();
        }
        const { patch, followUps, lyricsStale } = adapter.fold(m);
        if (Object.keys(patch).length > 0) Object.assign(np.value, patch);
        for (const frame of followUps) void sendFrame(frame);
        // Refetch lyrics on a track change when the overlay is open.
        if (lyricsStale && showLyrics.value) void fetchLyrics();
      }),
    );
    unlisten.push(
      await onState("primary", (s) => {
        const was = connected.value;
        connected.value = s.connected;
        if (s.connected && !was) {
          // Snapshot the primary connection's protocol for this session.
          adapter = createPlayerAdapter(useDirectStore().protocolVersion);
          reset();
          awaitingHandshake = true;
          startPolling();
        } else if (!s.connected && was) {
          awaitingHandshake = false;
          adapter = null;
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
