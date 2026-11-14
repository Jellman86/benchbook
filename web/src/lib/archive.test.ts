import { describe, expect, it } from "vitest";
import { refusalWords } from "./archive";

describe("refusals", () => {
  it("names the places still inside", () => {
    expect(refusalWords("HasPlaces", 2)).toBe(
      "It still has 2 places inside it. Move them, or stop using them, first.",
    );
  });

  it("names a single tray still there", () => {
    expect(refusalWords("HasTrays", 1)).toBe(
      "It still has a tray on it. Move the tray somewhere else first.",
    );
  });
});
