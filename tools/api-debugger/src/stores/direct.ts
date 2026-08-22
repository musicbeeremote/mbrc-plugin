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
import { V6_INIT_OPS } from "../lib/commands.v6";

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
  // When a V6 channel finishes its (backend-driven) handshake, replay the read
  // burst a real client sends on connect. On by default; toggle off to inspect a
  // bare handshake. V6 only - the legacy path has no equivalent here.
  const autoInitV6 = ref(true);

  // A persisted per-install id for V6 connections (generated once, reused across
  // relaunches - mirrors how a real client persists its client_id).
  const CLIENT_ID_KEY = "mbrc-v6-client-id";
  let clientId = localStorage.getItem(CLIENT_ID_KEY) ?? "";
  if (!clientId) {
    clientId = crypto.randomUUID();
    localStorage.setItem(CLIENT_ID_KEY, clientId);
  }
  // Per-connection V6 request-id counter (client ops start at 1; the handshake is
  // id 0). Reset each time a channel connects.
  const v6Ids: Record<Channel, number> = { primary: 1, secondary: 1 };

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
    v6Ids[channel] = 1; // fresh id sequence per connection
    try {
      await apiConnect(channel, {
        host: host.value,
        port: port.value,
        client_type: clientType.value,
        protocol_version: protocolVersion.value,
        // Secondary is always no_broadcast (data-fetch pattern).
        no_broadcast: channel === "secondary" ? true : noBroadcast.value,
        client_id: clientId,
      });
    } catch (e) {
      statusOf(channel).value = `Error: ${e}`;
      push(channel, "error", "connect", String(e));
    }
  }

  async function disconnect(channel: Channel) {
    await apiDisconnect(channel);
  }

  /**
   * Fire the post-handshake init burst on a freshly-connected V6 channel, each
   * as a proper `{kind:"request", id, op, data:{}}` envelope with a correlation
   * id from the channel's sequence. Sent in order; a failed send is logged but
   * doesn't abort the rest.
   */
  async function runV6Init(channel: Channel) {
    for (const op of V6_INIT_OPS) {
      const env = { kind: "request", id: v6Ids[channel]++, op, data: {} };
      try {
        await apiSendCommand(channel, JSON.stringify(env));
      } catch (e) {
        push(channel, "error", op, String(e));
      }
    }
  }

  async function send() {
    const ch = sendTarget.value;
    if (!connectedOf(ch).value) return;
    const raw = command.value.trim();
    if (!raw) return;
    let json = raw;
    // V6 sends an envelope: force kind:"request" and inject the correlation id, so
    // the composer template only needs `op`/`data`.
    if (protocolVersion.value === 6) {
      try {
        const env = JSON.parse(raw) as Record<string, unknown>;
        env.kind = "request";
        env.id = v6Ids[ch]++;
        json = JSON.stringify(env);
      } catch (e) {
        push(ch, "error", "send", `invalid V6 envelope JSON: ${e}`);
        return;
      }
    }
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
          const was = connectedOf(ch).value;
          connectedOf(ch).value = s.connected;
          statusOf(ch).value = s.connected ? "Connected" : (s.detail ?? "Disconnected");
          // On the false->true edge (handshake complete), replay a real client's
          // init burst for V6 - primary only (the secondary is the no_broadcast
          // data-fetch channel; it shouldn't fire the warm-up burst).
          if (ch === "primary" && s.connected && !was && protocolVersion.value === 6 && autoInitV6.value) {
            void runV6Init(ch);
          }
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
    autoInitV6,
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
