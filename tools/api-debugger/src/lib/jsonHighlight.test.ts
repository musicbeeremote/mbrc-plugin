import { describe, expect, it } from "vitest";
import { escapeHtml, highlightJson } from "./jsonHighlight";

describe("escapeHtml", () => {
  it("escapes markup-significant characters", () => {
    expect(escapeHtml('<a> & "b"')).toBe('&lt;a&gt; &amp; "b"');
  });
});

describe("highlightJson", () => {
  it("classifies keys vs string values", () => {
    const html = highlightJson('{\n  "context": "player"\n}');
    expect(html).toContain('<span class="tok-key">"context":</span>');
    expect(html).toContain('<span class="tok-str">"player"</span>');
  });

  it("classifies numbers, booleans and null", () => {
    const html = highlightJson('{ "a": 42, "b": true, "c": null, "d": -1.5e3 }');
    expect(html).toContain('<span class="tok-num">42</span>');
    expect(html).toContain('<span class="tok-bool">true</span>');
    expect(html).toContain('<span class="tok-null">null</span>');
    expect(html).toContain('<span class="tok-num">-1.5e3</span>');
  });

  it("escapes HTML in string payloads before wrapping (no injection)", () => {
    const html = highlightJson('{ "x": "<script>&" }');
    expect(html).not.toContain("<script>");
    expect(html).toContain("&lt;script&gt;&amp;");
  });

  it("returns non-JSON text escaped but safe", () => {
    expect(highlightJson("<not json>")).toBe("&lt;not json&gt;");
  });
});
