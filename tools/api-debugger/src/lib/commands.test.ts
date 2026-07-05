import { describe, expect, it } from "vitest";
import { COMMAND_CATALOG, findCommand } from "./commands";

const all = COMMAND_CATALOG.flatMap((g) => g.commands);

describe("COMMAND_CATALOG", () => {
  it("has non-empty groups", () => {
    expect(COMMAND_CATALOG.length).toBeGreaterThan(0);
    for (const g of COMMAND_CATALOG) expect(g.commands.length).toBeGreaterThan(0);
  });

  it("every template is valid JSON whose context matches, and carries a hint", () => {
    for (const cmd of all) {
      const parsed = JSON.parse(cmd.template) as { context: string; data: unknown };
      expect(parsed.context).toBe(cmd.context);
      expect(parsed).toHaveProperty("data");
      expect(cmd.hint.length).toBeGreaterThan(0);
    }
  });

  it("has no duplicate contexts", () => {
    const contexts = all.map((c) => c.context);
    expect(new Set(contexts).size).toBe(contexts.length);
  });

  it("marks a plausible set of Android-used commands", () => {
    const android = all.filter((c) => c.android).map((c) => c.context);
    // Core commands the 1.6.1 client sends…
    expect(android).toEqual(expect.arrayContaining(["playerstatus", "nowplayinglist", "libraryalbumcover", "browseartists"]));
    // …and commands it dropped are NOT marked.
    expect(android).not.toContain("playerautodj");
    expect(android).not.toContain("librarysearchtitle");
  });

  it("marks iOS-used commands, including the nav commands Android dropped", () => {
    const ios = all.filter((c) => c.ios).map((c) => c.context);
    expect(ios).toEqual(
      expect.arrayContaining(["libraryartistalbums", "libraryalbumtracks", "librarygenreartists", "nowplayinglistmove"]),
    );
    expect(ios).not.toContain("playerautodj");
  });
});

describe("findCommand", () => {
  it("resolves a composed message back to its catalog entry", () => {
    const entry = findCommand('{"context":"playervolume","data":50}');
    expect(entry?.context).toBe("playervolume");
    expect(entry?.android).toBe(true);
  });

  it("returns undefined for unknown contexts or malformed JSON", () => {
    expect(findCommand('{"context":"nope"}')).toBeUndefined();
    expect(findCommand("not json")).toBeUndefined();
  });
});
