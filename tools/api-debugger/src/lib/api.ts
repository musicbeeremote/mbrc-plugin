import { invoke } from "@tauri-apps/api/core";
import { listen, type UnlistenFn } from "@tauri-apps/api/event";
import { open } from "@tauri-apps/plugin-dialog";

export type Direction = "sent" | "received" | "info" | "error";

export interface WireMessage {
  direction: Direction;
  context: string;
  raw: string;
  // V6 envelope fields, present only for V6 frames (parsed by the backend).
  id?: number;
  kind?: string;
  op?: string;
  event?: string;
  error_code?: string;
}

export interface StateEvent {
  connected: boolean;
  detail?: string;
}

export interface ConnectOptions {
  host: string;
  port: number;
  client_type: string;
  protocol_version: number;
  no_broadcast: boolean;
  /** Per-install id for a V6 connection (ignored by legacy). */
  client_id?: string;
}

export type ProxyDir = "c2s" | "s2c";

/**
 * One captured wire frame - mirrors the `mbrc-capture/2` golden-trace `frame`
 * record on disk.
 */
export interface ProxyRecord {
  type: "frame";
  /** True TCP connection id, assigned at accept. */
  conn_id: number;
  seq: number;
  ts: string;
  dir: ProxyDir;
  elapsed_ms: number;
  /** For s2c frames: seq of the most recent c2s on this connection, if any. */
  reply_to?: number;
  /** Exact frame content as it crossed the wire (terminator stripped). */
  raw: string;
  /** Parsed JSON, present when `raw` was valid JSON. */
  frame?: unknown;
}

/**
 * What the UI receives per frame: a golden {@link ProxyRecord} flattened with
 * the client `peer` (`ip:port`; the port distinguishes same-client sockets).
 * `peer` is UI-only - it is NOT written to the capture file.
 */
export interface ProxyEvent extends ProxyRecord {
  peer: string;
}

/** A client connection opening/closing, so the UI can track the live set. */
export interface ConnChange {
  id: number;
  /** true = connected, false = disconnected. */
  open: boolean;
}

export interface ProxyStateEvent {
  listening: boolean;
  detail?: string;
  /**
   * Present on connection events (connect/disconnect); absent on server-state
   * events (start/stop/errors). Connection events must NOT drive `listening`.
   */
  conn?: ConnChange;
}

export interface ProxyOptions {
  listen: string;
  upstream: string;
  /** Optional golden-trace JSONL capture file. */
  output?: string | null;
}

const EVENT_MESSAGE = "mbrc://message";
const EVENT_STATE = "mbrc://state";
const EVENT_PROXY = "mbrc://proxy";
const EVENT_PROXY_STATE = "mbrc://proxy-state";

/** Independent direct-connection slots (each its own socket + event channels). */
export type ConnectionSlot = "primary" | "secondary";

export function connect(slot: ConnectionSlot, options: ConnectOptions): Promise<void> {
  return invoke("connect", { slot, options });
}

export function disconnect(slot: ConnectionSlot): Promise<void> {
  return invoke("disconnect", { slot });
}

export function sendCommand(slot: ConnectionSlot, json: string): Promise<void> {
  return invoke("send_command", { slot, json });
}

export function onMessage(slot: ConnectionSlot, handler: (msg: WireMessage) => void): Promise<UnlistenFn> {
  return listen<WireMessage>(`${EVENT_MESSAGE}/${slot}`, (e) => handler(e.payload));
}

export function onState(slot: ConnectionSlot, handler: (state: StateEvent) => void): Promise<UnlistenFn> {
  return listen<StateEvent>(`${EVENT_STATE}/${slot}`, (e) => handler(e.payload));
}

export function startProxy(options: ProxyOptions): Promise<void> {
  return invoke("start_proxy", { options });
}

export function stopProxy(): Promise<void> {
  return invoke("stop_proxy");
}

export function onProxy(handler: (record: ProxyEvent) => void): Promise<UnlistenFn> {
  return listen<ProxyEvent>(EVENT_PROXY, (e) => handler(e.payload));
}

export function onProxyState(handler: (state: ProxyStateEvent) => void): Promise<UnlistenFn> {
  return listen<ProxyStateEvent>(EVENT_PROXY_STATE, (e) => handler(e.payload));
}

// ── sessions ────────────────────────────────────────────────────────────────

/** A saved session file in the managed sessions directory. */
export interface SessionInfo {
  name: string;
  path: string;
  bytes: number;
  modified_ms: number;
  frames: number;
}

export function listSessions(): Promise<SessionInfo[]> {
  return invoke("list_sessions");
}

export function saveSession(name: string, contents: string): Promise<SessionInfo> {
  return invoke("save_session", { name, contents });
}

export function readSession(path: string): Promise<string> {
  return invoke("read_session", { path });
}

export function deleteSession(path: string): Promise<void> {
  return invoke("delete_session", { path });
}

export function importSession(src: string): Promise<SessionInfo> {
  return invoke("import_session", { src });
}

/**
 * Open a native file picker for a capture to import. Returns the chosen path,
 * or null if the user cancelled.
 */
export async function pickSessionFile(): Promise<string | null> {
  const selection = await open({
    multiple: false,
    directory: false,
    title: "Import capture",
    filters: [{ name: "Capture (JSONL)", extensions: ["jsonl", "json"] }],
  });
  return typeof selection === "string" ? selection : null;
}

// ── discovery ───────────────────────────────────────────────────────────────

/** A MusicBee Remote instance found via UDP-multicast discovery. */
export interface Discovered {
  address: string;
  port: number;
  name: string;
}

export function discover(timeoutMs?: number): Promise<Discovered[]> {
  return invoke("discover", { timeoutMs });
}
