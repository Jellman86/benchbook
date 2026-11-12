import { describe, expect, it } from "vitest";
import { columnsOf, sheetsNeeded } from "./labelSheets";

describe("label sheets", () => {
  it("knows how many stickers go across", () => {
    expect(columnsOf({ widthMm: 99.1 })).toBe(2);
    expect(columnsOf({ widthMm: 63.5 })).toBe(3);
    expect(columnsOf({ widthMm: 38.1 })).toBe(5);
  });

  it("counts the sheets needed from a part-used one", () => {
    expect(sheetsNeeded(21, 21, 1)).toBe(1);
    expect(sheetsNeeded(3, 21, 20)).toBe(2);
    expect(sheetsNeeded(0, 21, 1)).toBe(0);
  });
});
