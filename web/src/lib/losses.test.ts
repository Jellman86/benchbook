import { describe, expect, it } from "vitest";
import { lossWords } from "./losses";

describe("loss words", () => {
  it("adds trays lost before they were counted", () => {
    expect(lossWords(12, 0)).toBe("12");
    expect(lossWords(12, 1)).toBe("12 + 1 uncounted tray");
    expect(lossWords(0, 3)).toBe("3 uncounted trays");
  });
});
