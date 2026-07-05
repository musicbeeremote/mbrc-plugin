import { computed, ref } from "vue";
import { acceptHMRUpdate, defineStore } from "pinia";
import type { UnlistenFn } from "@tauri-apps/api/event";
import {
  connect as apiConnect,
  disconnect as apiDisconnect,
  sendCommand as apiSendCommand,
  discover as apiDiscover,
  onMessage,
  onState,
  type ConnectionSlot,
  type Direction,
  type Discovered,
} from "../lib/api";

/** Which connection a log entry / send belongs to. */
export type Channel = ConnectionSlot;

export interface LogEntry {
  id: number;
  time: string;
  channel: Channel;
  direction: Direction;
  context: string;
  raw: string;
}

/** Cap the merged in-memory log so long sessions don't grow unbounded. */
export const MAX_LOG = 2000;

/**
 * App-lifetime store for the Direct panel. Manages TWO sockets - the primary
 * and a secondary (the Android data-fetch pattern: same target, no_broadcast) -
 * merging both into ONE channel-tagged log. The panel filters the log by
 * channel; sends go to the selected channel. State lives here so it survives
 * tab switches and accumulates even while the panel is closed.
 */
export const useDirectStore = defineStore("direct", () => {
  // ── merged log ─────────────────────────────────────────────────────────────
  const log = ref<LogEntry[]>([]);
  const selected = ref<LogEntry | null>(null);
  let nextId = 0;

  // ── per-channel connection state ────────────────────────────────────────────
  const primaryConnected = ref(false);
  const primaryStatus = ref("Disconnected");
  const secondaryConnected = ref(false);
  const secondaryStatus = ref("Disconnected");

  function connectedOf(ch: Channel) {
    return ch === "primary" ? primaryConnected : secondaryConnected;
  }
  function statusOf(ch: Channel) {
    return ch === "primary" ? primaryStatus : secondaryStatus;
  }

  // ── connection config (the primary form; secondary follows the same target) ─
  const host = ref("127.0.0.1");
  const port = ref(3000);
  const clientType = ref("Android");
  const protocolVersion = ref(4);
  const noBroadcast = ref(false);

  // ── composer ────────────────────────────────────────────────────────────────
  const command = ref('{"context":"verifyconnection","data":{}}');
  /** Which channel the composer sends over. */
  const sendTarget = ref<Channel>("primary");

  // ── log channel filter ──────────────────────────────────────────────────────
  const channelFilter = ref<"all" | Channel>("all");
  const filteredLog = computed(() =>
    channelFilter.value === "all"
      ? log.value
      : log.value.filter((e) => e.channel === channelFilter.value),
  );

  // ── discovery ────────────────────────────────────────────────────────────────
  const discovered = ref<Discovered[]>([]);
  const discovering = ref(false);

  async function discover() {
    discovering.value = true;
    try {
      discovered.value = await apiDiscover();
    } catch (e) {
      push("primary", "error", "discover", String(e));
    } finally {
      discovering.value = false;
    }
  }

  function useDiscovered(d: Discovered) {
    host.value = d.address;
    port.value = d.port;
  }

  function push(channel: Channel, direction: Direction, context: string, raw: string) {
    log.value.push({ id: nextId++, time: new Date().toLocaleTimeString(), channel, direction, context, raw });
    if (log.value.length > MAX_LOG) log.value.splice(0, log.value.length - MAX_LOG);
  }

  function clear() {
    log.value = [];
    selected.value = null;
  }

  async function connect(channel: Channel) {
    statusOf(channel).value = "Connecting...";
    try {
      await apiConnect(channel, {
        host: host.value,
        port: port.value,
        client_type: clientType.value,
        protocol_version: protocolVersion.value,
        // Secondary is always no_broadcast (data-fetch pattern).
        no_broadcast: channel === "secondary" ? true : noBroadcast.value,
      });
    } catch (e) {
      statusOf(channel).value = `Error: ${e}`;
      push(channel, "error", "connect", String(e));
    }
  }

  async function disconnect(channel: Channel) {
    await apiDisconnect(channel);
  }

  async function send() {
    const ch = sendTarget.value;
    if (!connectedOf(ch).value) return;
    const json = command.value.trim();
    if (!json) return;
    try {
      await apiSendCommand(ch, json);
    } catch (e) {
      push(ch, "error", "send", String(e));
    }
  }

  // ── one-time event subscription for both channels ───────────────────────────
  let subscribed = false;
  const unlisten: UnlistenFn[] = [];

  async function init() {
    if (subscribed) return;
    subscribed = true;
    for (const ch of ["primary", "secondary"] as const) {
      unlisten.push(await onMessage(ch, (m) => push(ch, m.direction, m.context, m.raw)));
      unlisten.push(
        await onState(ch, (s) => {
          connectedOf(ch).value = s.connected;
          statusOf(ch).value = s.connected ? "Connected" : (s.detail ?? "Disconnected");
        }),
      );
    }
  }

  function dispose() {
    unlisten.forEach((u) => u());
    unlisten.length = 0;
    subscribed = false;
  }

  return {
    log,
    filteredLog,
    selected,
    primaryConnected,
    primaryStatus,
    secondaryConnected,
    secondaryStatus,
    host,
    port,
    clientType,
    protocolVersion,
    noBroadcast,
    command,
    sendTarget,
    channelFilter,
    discovered,
    discovering,
    push,
    clear,
    connect,
    disconnect,
    send,
    discover,
    useDiscovered,
    init,
    dispose,
  };
});

if (import.meta.hot) {
  import.meta.hot.accept(acceptHMRUpdate(useDirectStore, import.meta.hot));
  import.meta.hot.dispose(() => {
    try {
      useDirectStore().dispose();
    } catch {
      /* store not instantiated in this module context */
    }
  });
}
