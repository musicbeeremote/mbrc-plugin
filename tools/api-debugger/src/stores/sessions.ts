import { computed, ref } from "vue";
import { defineStore, acceptHMRUpdate } from "pinia";
import {
  listSessions,
  saveSession,
  readSession,
  deleteSession,
  importSession,
  type SessionInfo,
} from "../lib/api";
import { compareSchemas, parseSession, type SchemaComparison, type SessionFrame } from "../lib/diff";
import { directLogToJsonl, proxyRowsToJsonl } from "../lib/serialize";
import { useProxyStore } from "./proxy";
import { useDirectStore } from "./direct";

/**
 * Store for the Sessions + Compare panels: the saved-session list, the loaded
 * viewer frames, and the A/B compare selection + result. State lives here so it
 * survives tab switches. No live subscription - this is request/response over
 * the backend session commands.
 */
export const useSessionsStore = defineStore("sessions", () => {
  const sessions = ref<SessionInfo[]>([]);
  const loading = ref(false);
  const error = ref<string | null>(null);

  // ── viewer ───────────────────────────────────────────────────────────────
  const viewerPath = ref<string | null>(null);
  const viewerName = ref<string>("");
  const viewerFrames = ref<SessionFrame[]>([]);
  const viewerSelected = ref<SessionFrame | null>(null);

  // ── compare ──────────────────────────────────────────────────────────────
  const compareA = ref<string | null>(null);
  const compareB = ref<string | null>(null);
  const diff = ref<SchemaComparison | null>(null);
  const diffError = ref<string | null>(null);

  const byPath = computed(() => new Map(sessions.value.map((s) => [s.path, s])));

  async function refresh() {
    loading.value = true;
    error.value = null;
    try {
      sessions.value = await listSessions();
    } catch (e) {
      error.value = String(e);
    } finally {
      loading.value = false;
    }
  }

  async function open(info: SessionInfo) {
    error.value = null;
    try {
      const text = await readSession(info.path);
      viewerFrames.value = parseSession(text);
      viewerPath.value = info.path;
      viewerName.value = info.name;
      viewerSelected.value = null;
    } catch (e) {
      error.value = String(e);
    }
  }

  async function remove(info: SessionInfo) {
    try {
      await deleteSession(info.path);
      if (viewerPath.value === info.path) {
        viewerPath.value = null;
        viewerFrames.value = [];
        viewerSelected.value = null;
      }
      if (compareA.value === info.path) compareA.value = null;
      if (compareB.value === info.path) compareB.value = null;
      await refresh();
    } catch (e) {
      error.value = String(e);
    }
  }

  async function saveProxy(name: string) {
    error.value = null;
    try {
      await saveSession(name, proxyRowsToJsonl(useProxyStore().rows));
      await refresh();
    } catch (e) {
      error.value = String(e);
    }
  }

  async function saveDirect(name: string) {
    error.value = null;
    try {
      // Save the primary channel's traffic as the direct session.
      await saveSession(
        name,
        directLogToJsonl(useDirectStore().log.filter((e) => e.channel === "primary")),
      );
      await refresh();
    } catch (e) {
      error.value = String(e);
    }
  }

  async function importPath(src: string) {
    error.value = null;
    try {
      await importSession(src.trim());
      await refresh();
    } catch (e) {
      error.value = String(e);
    }
  }

  async function runCompare() {
    diffError.value = null;
    diff.value = null;
    if (!compareA.value || !compareB.value) {
      diffError.value = "Pick two sessions to compare.";
      return;
    }
    try {
      const [ta, tb] = await Promise.all([readSession(compareA.value), readSession(compareB.value)]);
      diff.value = compareSchemas(parseSession(ta), parseSession(tb));
    } catch (e) {
      diffError.value = String(e);
    }
  }

  return {
    sessions,
    loading,
    error,
    byPath,
    viewerPath,
    viewerName,
    viewerFrames,
    viewerSelected,
    compareA,
    compareB,
    diff,
    diffError,
    refresh,
    open,
    remove,
    saveProxy,
    saveDirect,
    importPath,
    runCompare,
  };
});

if (import.meta.hot) {
  import.meta.hot.accept(acceptHMRUpdate(useSessionsStore, import.meta.hot));
}
