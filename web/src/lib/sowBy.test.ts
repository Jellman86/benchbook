import { describe, expect, it } from "vitest";
import { monthEnd, sowByParts, sowByPassed, sowByWords } from "./sowBy";

describe("sow-by dates", () => {
  it("run to the end of the month a packet gives", () => {
    expect(monthEnd(2027, 12)).toBe("2027-12-31");
    expect(monthEnd(2028, 2)).toBe("2028-02-29");
  });

  it("read back as a month and a year", () => {
    expect(sowByParts("2027-12-31")).toEqual({ year: 2027, month: 12 });
    expect(sowByParts("")).toBeNull();
    expect(sowByWords("2027-12-31")).toBe("Dec 2027");
  });

  it("say when a packet's date has passed", () => {
    const now = new Date(2026, 10, 12);
    expect(sowByPassed("2026-10-31", now)).toBe(true);
    expect(sowByPassed("2026-11-30", now)).toBe(false);
    expect(sowByPassed("", now)).toBe(false);
  });
});
