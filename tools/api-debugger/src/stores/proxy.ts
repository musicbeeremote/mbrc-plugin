import { computed, ref } from "vue";
import { acceptHMRUpdate, defineStore } from "pinia";
import type { UnlistenFn } from "@tauri-apps/api/event";
import {
  startProxy as apiStartProxy,
  stopProxy as apiStopProxy,
  onProxy,
  onProxyState,
  type ProxyDir,
  type ProxyEvent,
} from "../lib/api";

/** A captured frame plus a stable list id + precomputed display context. */
export interface ProxyEntry extends ProxyEvent {
  id: number;
  context: string;
}

/**
 * Hard cap on the in-memory frame buffer. When `push` crosses it, the oldest
 * entries are evicted from the front (ring-buffer semantics) so a long capture
 * session can't grow the JS heap without bound. The on-disk JSONL capture is
 * uncapped - this only bounds what the UI holds.
 */
export const MAX_ROWS = 5000;

/** Pull a display `context` out of a frame's parsed JSON. */
function contextOf(rec: ProxyEvent): string {
  const f = rec.frame;
  if (f && typeof f === "object" && "context" in f) {
    const c = (f as { context?: unknown }).context;
    if (typeof c === "string") return c;
  }
  // `raw` is always present; a missing parse means malformed (non-JSON) bytes.
  return rec.frame === undefined ? "raw" : "-";
}

/**
 * App-lifetime store for the proxy view. Owning the buffer + the Tauri event
 * subscription here (not in the panel) means frames keep accumulating across
 * tab switches and even before the panel is first opened - the component is a
 * pure view that rehydrates from this store on mount.
 */
export const useProxyStore = defineStore("proxy", () => {
  // ── captured frames ──────────────────────────────────────────────────────
  const rows = ref<ProxyEntry[]>([]);
  const selected = ref<ProxyEntry | null>(null);
  let nextId = 0;

  // ── proxy lifecycle ──────────────────────────────────────────────────────
  const listening = ref(false);
  // Last activity notice from the backend (rolling; e.g. "client connected …").
  const status = ref("Stopped");
  // Ids of currently-connected clients - the authoritative "connected" state,
  // maintained from structured connection events (not parsed from `status`).
  const activeConns = ref<Set<number>>(new Set());

  const connectedCount = computed(() => activeConns.value.size);

  /** Stable, authoritative state label for the header indicator. */
  const stateLabel = computed(() => {
    if (!listening.value) return "Stopped";
    const n = connectedCount.value;
    return n === 0 ? "Listening" : `Listening · ${n} connected`;
  });

  // ── config (persists across tab switches) ────────────────────────────────
  const listen = ref("0.0.0.0:3100");
  const upstream = ref("127.0.0.1:3000");
  const captureEnabled = ref(false);
  const output = ref("session.jsonl");

  // ── filters / view prefs ─────────────────────────────────────────────────
  const followTail = ref(true);
  const dirFilter = ref<"all" | ProxyDir>("all");
  const connFilter = ref<"all" | number>("all");
  const search = ref("");

  // Distinct connections seen so far, with peer, for the conn filter dropdown.
  const connections = computed(() => {
    const map = new Map<number, string>();
    for (const r of rows.value) if (!map.has(r.conn_id)) map.set(r.conn_id, r.peer);
    return [...map.entries()].map(([conn_id, peer]) => ({ conn_id, peer }));
  });

  const filteredRows = computed(() => {
    const dir = dirFilter.value;
    const conn = connFilter.value;
    const q = search.value.trim().toLowerCase();
    if (dir === "all" && conn === "all" && !q) return rows.value;
    return rows.value.filter((r) => {
      if (dir !== "all" && r.dir !== dir) return false;
      if (conn !== "all" && r.conn_id !== conn) return false;
      if (q && !r.context.toLowerCase().includes(q) && !r.raw.toLowerCase().includes(q)) {
        return false;
      }
      return true;
    });
  });

  function push(rec: ProxyEvent) {
    rows.value.push({ ...rec, id: nextId++, context: contextOf(rec) });
    // Evict oldest once over the cap (ring buffer).
    if (rows.value.length > MAX_ROWS) {
      rows.value.splice(0, rows.value.length - MAX_ROWS);
    }
  }

  function clear() {
    rows.value = [];
    selected.value = null;
  }

  async function start() {
    try {
      status.value = "Starting...";
      // Fresh session: the backend truncates the capture and restarts conn ids.
      activeConns.value = new Set();
      await apiStartProxy({
        listen: listen.value,
        upstream: upstream.value,
        output: captureEnabled.value ? output.value : null,
      });
    } catch (e) {
      status.value = `Error: ${e}`;
    }
  }

  async function stop() {
    await apiStopProxy();
  }

  // ── one-time event subscription ──────────────────────────────────────────
  // Subscribe once for the app's lifetime so the buffer fills regardless of
  // which tab is active. `init()` is idempotent; call it at app startup.
  let subscribed = false;
  let unlistenProxy: UnlistenFn | null = null;
  let unlistenState: UnlistenFn | null = null;

  async function init() {
    if (subscribed) return;
    subscribed = true;
    unlistenProxy = await onProxy(push);
    unlistenState = await onProxyState((s) => {
      if (s.conn) {
        // Connection event: update the live set only - never touch `listening`
        // (a disconnect emitted while stopping must not flip it back on).
        const next = new Set(activeConns.value);
        if (s.conn.open) next.add(s.conn.id);
        else next.delete(s.conn.id);
        activeConns.value = next;
      } else {
        // Server-state event drives the listening flag; a stop clears the set.
        listening.value = s.listening;
        if (!s.listening) activeConns.value = new Set();
      }
      if (s.detail) status.value = s.detail;
    });
  }

  /** Tear down subscriptions - used only on HMR dispose to avoid leaks. */
  function dispose() {
    unlistenProxy?.();
    unlistenState?.();
    unlistenProxy = null;
    unlistenState = null;
    subscribed = false;
  }

  return {
    rows,
    selected,
    listening,
    status,
    activeConns,
    connectedCount,
    stateLabel,
    listen,
    upstream,
    captureEnabled,
    output,
    followTail,
    dirFilter,
    connFilter,
    search,
    connections,
    filteredRows,
    push,
    clear,
    start,
    stop,
    init,
    dispose,
  };
});

// HMR: dispose the old store's event subscription before the module reloads, so
// `tauri dev` hot updates don't stack duplicate `mbrc://proxy` listeners.
if (import.meta.hot) {
  import.meta.hot.accept(acceptHMRUpdate(useProxyStore, import.meta.hot));
  import.meta.hot.dispose(() => {
    // The store instance may be gone; guard defensively.
    try {
      useProxyStore().dispose();
    } catch {
      /* store not instantiated in this module context */
    }
  });
}
