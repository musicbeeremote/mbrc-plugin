import { describe, expect, it } from "vitest";
import type { ProxyRecord } from "./api";
import { directLogToJsonl, proxyRowsToJsonl, type DirectLogEntry } from "./serialize";
import { compareSchemas, parseSession } from "./diff";

const proxyRow = (over: Partial<ProxyRecord>): ProxyRecord => ({
  type: "frame",
  conn_id: 0,
  seq: 0,
  ts: "2026-07-04T00:00:00Z",
  dir: "c2s",
  elapsed_ms: 0,
  raw: "{}",
  frame: {},
  ...over,
});

describe("proxyRowsToJsonl", () => {
  it("emits one golden frame line per row, round-tripping through parseSession", () => {
    const rows = [
      proxyRow({ seq: 0, dir: "c2s", raw: '{"context":"player"}', frame: { context: "player" } }),
      proxyRow({ seq: 1, dir: "s2c", reply_to: 0, raw: '{"context":"player"}', frame: { context: "player" } }),
    ];
    const jsonl = proxyRowsToJsonl(rows);
    expect(jsonl.split("\n")).toHaveLength(2);

    const frames = parseSession(jsonl);
    expect(frames).toHaveLength(2);
    expect(frames[1]).toMatchObject({ seq: 1, dir: "s2c", context: "player" });
    // A round-trip must be schema-identical to itself.
    expect(compareSchemas(frames, frames).changed).toHaveLength(0);
  });

  it("omits reply_to and frame when absent", () => {
    const [line] = proxyRowsToJsonl([proxyRow({ frame: undefined, reply_to: undefined, raw: "{bad" })]).split("\n");
    const parsed = JSON.parse(line);
    expect(parsed).not.toHaveProperty("reply_to");
    expect(parsed).not.toHaveProperty("frame");
    expect(parsed.raw).toBe("{bad");
  });
});

describe("directLogToJsonl", () => {
  const log: DirectLogEntry[] = [
    { direction: "sent", time: "t0", raw: '{"context":"playerstatus"}' },
    { direction: "info", time: "t1", raw: "connected" },
    { direction: "received", time: "t2", raw: '{"context":"playerstatus","data":{}}' },
    { direction: "error", time: "t3", raw: "boom" },
  ];

  it("keeps only sent/received, maps directions, and densely reassigns seq", () => {
    const frames = parseSession(directLogToJsonl(log));
    expect(frames.map((f) => f.dir)).toEqual(["c2s", "s2c"]);
    expect(frames.map((f) => f.seq)).toEqual([0, 1]);
    expect(frames[0].context).toBe("playerstatus");
  });

  it("leaves frame undefined for non-JSON raw payloads", () => {
    const frames = parseSession(directLogToJsonl([{ direction: "sent", time: "t", raw: "not json" }]));
    expect(frames[0].frame).toBeUndefined();
    expect(frames[0].context).toBe("raw");
  });
});
