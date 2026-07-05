import { describe, expect, it } from "vitest";
import { emptyNowPlaying, foldResponse, formatMs, shuffleActive } from "./player";

/** Apply a sequence of (context, data) responses onto a fresh state. */
function fold(seq: Array<[string, unknown]>) {
  const state = emptyNowPlaying();
  for (const [ctx, data] of seq) Object.assign(state, foldResponse(ctx, data));
  return state;
}

describe("foldResponse", () => {
  it("folds a track", () => {
    const s = fold([["nowplayingtrack", { artist: "ProleteR", title: "Downtown Irony", album: "Curses", year: "2012" }]]);
    expect(s).toMatchObject({ artist: "ProleteR", title: "Downtown Irony", album: "Curses", year: "2012" });
  });

  it("folds playerstatus fields", () => {
    const s = fold([
      ["playerstatus", { playermute: false, playerrepeat: "All", playershuffle: "shuffle", playerstate: "Playing", playervolume: "72" }],
    ]);
    expect(s.repeat).toBe("All");
    expect(s.shuffle).toBe("shuffle");
    expect(s.state).toBe("Playing");
    expect(s.volume).toBe(72);
    expect(s.muted).toBe(false);
  });

  it("parses position ms", () => {
    const s = fold([["nowplayingposition", { current: 79000, total: 261000 }]]);
    expect(s.positionMs).toBe(79000);
    expect(s.durationMs).toBe(261000);
  });

  it("decodes a base64 cover object into a data URL", () => {
    const png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk";
    const s = fold([["nowplayingcover", { cover: png, status: "200" }]]);
    expect(s.coverDataUrl).toBe(`data:image/png;base64,${png}`);
    expect(s.coverStatus).toBe("cover received");
  });

  it("keeps existing cover on a status-only reply", () => {
    const png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk";
    const state = emptyNowPlaying();
    Object.assign(state, foldResponse("nowplayingcover", { cover: png }));
    Object.assign(state, foldResponse("nowplayingcover", { status: 1 }));
    expect(state.coverDataUrl).toBe(`data:image/png;base64,${png}`);
    expect(state.coverStatus).toBe("cover ready (not included)");
  });

  it("handles the 404 lyrics reply", () => {
    const s = fold([["nowplayinglyrics", { lyrics: "", status: 404 }]]);
    expect(s.lyrics).toBe("");
    expect(s.lyricsStatus).toBe("no lyrics available");
  });

  it("folds lyrics text", () => {
    const s = fold([["nowplayinglyrics", { lyrics: "line one\nline two" }]]);
    expect(s.lyrics).toBe("line one\nline two");
  });

  it("parses shuffle from a legacy bool", () => {
    expect(foldResponse("playershuffle", true).shuffle).toBe("shuffle");
    expect(foldResponse("playershuffle", false).shuffle).toBe("off");
  });

  it("clamps volume", () => {
    expect(foldResponse("playervolume", 150).volume).toBe(100);
    expect(foldResponse("playervolume", -5).volume).toBe(0);
  });

  it("ignores unknown contexts and malformed data", () => {
    expect(foldResponse("browsegenres", { data: [] })).toEqual({});
    expect(foldResponse("nowplayingtrack", "nope")).toEqual({});
  });
});

describe("formatMs", () => {
  it("formats under an hour as m:ss", () => {
    expect(formatMs(79000)).toBe("1:19");
    expect(formatMs(261000)).toBe("4:21");
    expect(formatMs(0)).toBe("0:00");
  });
  it("formats past an hour as h:mm:ss", () => {
    expect(formatMs(3_661_000)).toBe("1:01:01");
  });
  it("guards against bad input", () => {
    expect(formatMs(-1)).toBe("0:00");
    expect(formatMs(NaN)).toBe("0:00");
  });
});

describe("shuffleActive", () => {
  it("is true for shuffle and autodj", () => {
    expect(shuffleActive("shuffle")).toBe(true);
    expect(shuffleActive("autodj")).toBe(true);
    expect(shuffleActive("off")).toBe(false);
  });
});
