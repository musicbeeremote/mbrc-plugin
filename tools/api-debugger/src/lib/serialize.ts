// Serialize live UI buffers into `mbrc-capture/2` JSONL, so a Proxy or Direct
// session can be saved and later re-parsed / diffed. Kept pure (input arrays,
// string output) so it's unit-testable without Pinia or the backend.

import type { Direction, ProxyRecord } from "./api";

/** Serialize proxy frame rows into JSONL (one golden frame record per line). */
export function proxyRowsToJsonl(rows: ProxyRecord[]): string {
  return rows.map(frameLine).join("\n");
}

function frameLine(r: ProxyRecord): string {
  const rec: Record<string, unknown> = {
    type: "frame",
    conn_id: r.conn_id,
    seq: r.seq,
    ts: r.ts,
    dir: r.dir,
    elapsed_ms: r.elapsed_ms,
    raw: r.raw,
  };
  if (r.reply_to !== undefined) rec.reply_to = r.reply_to;
  if (r.frame !== undefined) rec.frame = r.frame;
  return JSON.stringify(rec);
}

/** The subset of a direct-log entry needed to serialize it as a frame. */
export interface DirectLogEntry {
  direction: Direction;
  time: string;
  raw: string;
}

/**
 * Serialize a direct-connection log into frame JSONL: `sent` → `c2s`,
 * `received` → `s2c`; `info`/`error` entries are dropped (not wire frames).
 * `seq` is reassigned densely so the result is a self-consistent capture.
 */
export function directLogToJsonl(log: DirectLogEntry[]): string {
  const lines: string[] = [];
  let seq = 0;
  for (const e of log) {
    if (e.direction !== "sent" && e.direction !== "received") continue;
    let frame: unknown;
    try {
      frame = JSON.parse(e.raw);
    } catch {
      frame = undefined;
    }
    const rec: Record<string, unknown> = {
      type: "frame",
      conn_id: 0,
      seq: seq++,
      ts: e.time,
      dir: e.direction === "sent" ? "c2s" : "s2c",
      raw: e.raw,
    };
    if (frame !== undefined) rec.frame = frame;
    lines.push(JSON.stringify(rec));
  }
  return lines.join("\n");
}
