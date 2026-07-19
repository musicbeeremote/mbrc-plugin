import { describe, expect, it } from "vitest";
import type { WireMessage } from "./api";
import { emptyNowPlaying } from "./player";
import { createPlayerAdapter } from "./player-protocol";

/** A received V6 frame (response or event). */
function rx(fields: Partial<WireMessage> & { raw: string }): WireMessage {
  return { direction: "received", context: "", ...fields };
}

/** Parse a built request frame into its envelope. */
function env(json: string): { id: number; kind: string; op: string; data: Record<string, unknown> } {
  return JSON.parse(json);
}

describe("v4 adapter", () => {
  const a = createPlayerAdapter(4);

  it("emits legacy {context,data} frames and server-side toggles", () => {
    expect(JSON.parse(a.playPause())).toEqual({ context: "playerplaypause", data: "" });
    expect(JSON.parse(a.toggleMute(emptyNowPlaying()))).toEqual({ context: "playermute", data: "toggle" });
    expect(JSON.parse(a.setVolume(150))).toEqual({ context: "playervolume", data: 100 });
  });

  it("is ready on the protocol reply and folds a playerstatus frame", () => {
    expect(a.isReady(rx({ context: "protocol", raw: "{}" }))).toBe(true);
    const out = a.fold(
      rx({ context: "playerstatus", raw: JSON.stringify({ context: "playerstatus", data: { playerstate: "playing", playervolume: 40 } }) }),
    );
    expect(out.patch.state).toBe("Playing");
    expect(out.patch.volume).toBe(40);
  });
});

describe("v6 adapter", () => {
  it("builds envelopes with an id and computes explicit toggle targets", () => {
    const a = createPlayerAdapter(6);
    const np = emptyNowPlaying();
    np.muted = false;
    np.shuffle = "off";
    np.repeat = "All";
    np.lfm = "Love";
    expect(env(a.toggleMute(np)).op).toBe("player_set_mute");
    expect(env(a.toggleMute(np)).data).toEqual({ muted: true });
    expect(env(a.toggleShuffle(np)).data).toEqual({ mode: "shuffle" });
    expect(env(a.toggleRepeat(np)).data).toEqual({ mode: "one" }); // All -> One
    expect(env(a.toggleLove(np)).data).toEqual({ status: "normal" }); // Love -> normal
    const seek = env(a.seek(30000.7));
    expect(seek).toMatchObject({ kind: "request", op: "now_playing_seek", data: { position_ms: 30001 } });
    expect(typeof seek.id).toBe("number");
  });

  it("is ready on the id:0 handshake response only", () => {
    const a = createPlayerAdapter(6);
    expect(a.isReady(rx({ kind: "response", id: 0, raw: "{}" }))).toBe(true);
    expect(a.isReady(rx({ kind: "response", id: 5, raw: "{}" }))).toBe(false);
    expect(a.isReady(rx({ event: "volume_changed", raw: "{}" }))).toBe(false);
  });

  it("folds self-describing events", () => {
    const a = createPlayerAdapter(6);
    expect(a.fold(rx({ event: "play_state_changed", raw: JSON.stringify({ data: { play_state: "paused" } }) })).patch.state).toBe("Paused");
    expect(a.fold(rx({ event: "volume_changed", raw: JSON.stringify({ data: { volume: 55 } }) })).patch.volume).toBe(55);
    expect(a.fold(rx({ event: "mute_changed", raw: JSON.stringify({ data: { muted: true } }) })).patch.muted).toBe(true);
    // Track change patches title immediately, re-queries state, and stales lyrics.
    const ch = a.fold(rx({ event: "now_playing_changed", raw: JSON.stringify({ data: { title: "T", artist: "A", album: "Al" } }) }));
    expect(ch.patch.title).toBe("T");
    expect(ch.lyricsStale).toBe(true);
    expect(env(ch.followUps[0]).op).toBe("now_playing_state");
  });

  it("correlates a response to its op by id and chains a cover fetch", () => {
    const a = createPlayerAdapter(6);
    // Send now_playing_state so its id is registered for folding.
    const reqId = env(a.startup()[1]).id; // startup()[1] is now_playing_state
    const out = a.fold(
      rx({
        kind: "response",
        id: reqId,
        raw: JSON.stringify({
          id: reqId,
          kind: "response",
          data: {
            track: { title: "Song", artist: "Band", album: "Rec", year: 2003, cover_hash: "abc" },
            position_ms: 1000,
            duration_ms: 240000,
            lfm_status: "love",
          },
        }),
      }),
    );
    expect(out.patch.title).toBe("Song");
    expect(out.patch.year).toBe("2003");
    expect(out.patch.lfm).toBe("Love");
    expect(out.patch.durationMs).toBe(240000);
    // cover_hash present + new -> a cover_get follow-up.
    expect(env(out.followUps[0])).toMatchObject({ op: "cover_get", data: { hash: "abc" } });
  });

  it("folds setter replies (shuffle/repeat/lfm have no event)", () => {
    const a = createPlayerAdapter(6);
    const np = emptyNowPlaying();
    np.shuffle = "off";
    np.repeat = "None";
    np.lfm = "Normal";
    // Each setter registers its id; its reply echoes the new canonical value.
    const shufId = env(a.toggleShuffle(np)).id;
    const shuf = a.fold(rx({ kind: "response", id: shufId, raw: JSON.stringify({ id: shufId, kind: "response", data: { mode: "shuffle" } }) }));
    expect(shuf.patch.shuffle).toBe("shuffle");

    const repId = env(a.toggleRepeat(np)).id;
    const rep = a.fold(rx({ kind: "response", id: repId, raw: JSON.stringify({ id: repId, kind: "response", data: { mode: "all" } }) }));
    expect(rep.patch.repeat).toBe("All");

    const lfmId = env(a.toggleLove(np)).id;
    const lfm = a.fold(rx({ kind: "response", id: lfmId, raw: JSON.stringify({ id: lfmId, kind: "response", data: { lfm_status: "love" } }) }));
    expect(lfm.patch.lfm).toBe("Love");
  });

  it("ignores a response whose id it never sent", () => {
    const a = createPlayerAdapter(6);
    const out = a.fold(rx({ kind: "response", id: 999, raw: JSON.stringify({ id: 999, kind: "response", data: { volume: 1 } }) }));
    expect(out.patch).toEqual({});
    expect(out.followUps).toEqual([]);
  });

  it("folds structured lyrics into flat text", () => {
    const a = createPlayerAdapter(6);
    const id = env(a.fetchLyrics()).id;
    const out = a.fold(
      rx({
        kind: "response",
        id,
        raw: JSON.stringify({ id, kind: "response", data: { type: "synced", lines: [{ text: "one", at_ms: 0 }, { text: "two", at_ms: 900 }] } }),
      }),
    );
    expect(out.patch.lyrics).toBe("one\ntwo");
    expect(out.patch.lyricsStatus).toBe("synced");
  });
});
