import { describe, expect, it } from "vitest";
import { refusalWords } from "./archive";

describe("refusals", () => {
  it("names the places still inside", () => {
    expect(refusalWords("HasPlaces", 2)).toBe(
      "It still has 2 places inside it. Move or stop using those first.",
    );
  });

  it("names a single tray still there", () => {
    expect(refusalWords("HasTrays", 1)).toBe(
      "It still has 1 tray on it. Move them somewhere else first.",
    );
  });
});
