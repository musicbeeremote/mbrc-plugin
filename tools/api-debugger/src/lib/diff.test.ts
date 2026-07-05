import { describe, expect, it } from "vitest";
import { collectSchema, compareSchemas, diffSchemas, parseSession } from "./diff";

const frame = (conn_id: number, seq: number, dir: string, obj: unknown) =>
  JSON.stringify({ type: "frame", conn_id, seq, dir, raw: JSON.stringify(obj), frame: obj });

describe("parseSession", () => {
  it("parses frame lines and skips meta / blank / malformed lines", () => {
    const text = [
      '{"type":"meta","event":"capture-start"}',
      frame(0, 0, "c2s", { context: "player", data: "Android" }),
      "",
      "not json",
      frame(0, 1, "s2c", { context: "player", data: "MusicBee" }),
    ].join("\n");

    const frames = parseSession(text);
    expect(frames).toHaveLength(2);
    expect(frames[0]).toMatchObject({ conn_id: 0, seq: 0, dir: "c2s", context: "player" });
  });

  it("marks a raw-only (non-JSON) frame with context 'raw' and no parsed frame", () => {
    const line = JSON.stringify({ type: "frame", conn_id: 0, seq: 0, dir: "c2s", raw: "{bad" });
    const [f] = parseSession(line);
    expect(f.frame).toBeUndefined();
    expect(f.context).toBe("raw");
  });
});

describe("collectSchema", () => {
  it("records field paths with their value types, folding array elements to []", () => {
    const frames = parseSession(frame(0, 0, "s2c", { context: "x", data: { n: 1, items: [{ a: "s" }] } }));
    const schema = collectSchema(frames);
    expect(schema.get("$.data.n")).toEqual(new Set(["number"]));
    expect(schema.get("$.data.items")).toEqual(new Set(["array"]));
    expect(schema.get("$.data.items[].a")).toEqual(new Set(["string"]));
  });

  it("unions types seen across multiple frames of the same endpoint", () => {
    const frames = parseSession(
      [
        frame(0, 0, "s2c", { context: "x", v: 1 }),
        frame(0, 1, "s2c", { context: "x", v: null }),
      ].join("\n"),
    );
    expect(collectSchema(frames).get("$.v")).toEqual(new Set(["number", "null"]));
  });
});

describe("diffSchemas", () => {
  it("flags fields present on one side only and type changes", () => {
    const a = collectSchema(parseSession(frame(0, 0, "s2c", { context: "x", keep: 1, gone: "s" })));
    const b = collectSchema(parseSession(frame(0, 0, "s2c", { context: "x", keep: "1", added: true })));
    const diff = diffSchemas(a, b);
    expect(diff).toContainEqual({ path: "$.gone", kind: "missing", a: "string" });
    expect(diff).toContainEqual({ path: "$.added", kind: "extra", b: "boolean" });
    expect(diff).toContainEqual({ path: "$.keep", kind: "type", a: "number", b: "string" });
  });

  it("returns nothing for identical schemas", () => {
    const s = collectSchema(parseSession(frame(0, 0, "s2c", { context: "x", a: 1 })));
    expect(diffSchemas(s, s)).toEqual([]);
  });
});

describe("compareSchemas", () => {
  const a = parseSession(
    [
      frame(0, 0, "c2s", { context: "player", data: "Android" }),
      frame(0, 1, "s2c", { context: "playerstatus", data: { playing: true } }),
    ].join("\n"),
  );

  it("matches endpoints by (dir, context) regardless of seq/order", () => {
    // Same endpoints, but shuffled seq/order and duplicated - must still match.
    const b = parseSession(
      [
        frame(9, 7, "s2c", { context: "playerstatus", data: { playing: false } }),
        frame(9, 3, "c2s", { context: "player", data: "iOS" }),
        frame(9, 8, "s2c", { context: "playerstatus", data: { playing: true } }),
      ].join("\n"),
    );
    const diff = compareSchemas(a, b);
    expect(diff.matched).toBe(2);
    expect(diff.changed).toHaveLength(0);
    expect(diff.onlyA).toHaveLength(0);
    expect(diff.onlyB).toHaveLength(0);
  });

  it("reports an endpoint whose response schema changed", () => {
    const b = parseSession(
      [
        frame(0, 0, "c2s", { context: "player", data: "Android" }),
        frame(0, 1, "s2c", { context: "playerstatus", data: { playing: true, volume: 50 } }),
      ].join("\n"),
    );
    const diff = compareSchemas(a, b);
    expect(diff.changed).toHaveLength(1);
    expect(diff.changed[0].context).toBe("playerstatus");
    expect(diff.changed[0].fields).toContainEqual({ path: "$.data.volume", kind: "extra", b: "number" });
  });

  it("reports endpoints present on only one side", () => {
    const b = parseSession(frame(0, 0, "c2s", { context: "player", data: "Android" }));
    const diff = compareSchemas(a, b);
    expect(diff.onlyA.map((e) => e.context)).toEqual(["playerstatus"]);
    expect(diff.onlyB).toHaveLength(0);
  });

  it("distinguishes request (c2s) from response (s2c) for the same context", () => {
    const reqOnly = parseSession(frame(0, 0, "c2s", { context: "playerstatus", data: {} }));
    const respOnly = parseSession(frame(0, 0, "s2c", { context: "playerstatus", data: {} }));
    const diff = compareSchemas(reqOnly, respOnly);
    expect(diff.matched).toBe(0);
    expect(diff.onlyA).toHaveLength(1);
    expect(diff.onlyB).toHaveLength(1);
  });
});
