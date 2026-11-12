import { describe, expect, it } from "vitest";
import {
  addDays,
  addMonths,
  dayWords,
  isWithin,
  momentOn,
  monthGrid,
  onDay,
} from "./days";

const now = new Date(2026, 10, 12, 14, 30);

describe("monthGrid", () => {
  it("starts weeks on Monday", () => {
    // 1 November 2026 is a Sunday, so six blanks come first.
    const grid = monthGrid("2026-11-20");
    expect(grid.slice(0, 7)).toEqual([
      null,
      null,
      null,
      null,
      null,
      null,
      "2026-11-01",
    ]);
    expect(grid.at(-1)).toBe("2026-11-30");
  });

  it("knows February's length", () => {
    expect(monthGrid("2028-02-01").at(-1)).toBe("2028-02-29");
  });
});

describe("moving about", () => {
  it("crosses months and years", () => {
    expect(addDays("2026-12-31", 1)).toBe("2027-01-01");
    expect(addMonths("2026-01-31", -1)).toBe("2025-12-01");
  });
});

describe("dayWords", () => {
  it("says today and yesterday in words", () => {
    expect(dayWords("2026-11-12", now)).toBe("Today");
    expect(dayWords("2026-11-11", now)).toBe("Yesterday");
    expect(dayWords("2026-11-10", now)).toBe("Tue 10 Nov");
  });

  it("follows a word such as Sown", () => {
    expect(onDay("2026-11-11", now)).toBe("yesterday");
    expect(onDay("2026-11-10", now)).toBe("on Tue 10 Nov");
  });
});

describe("momentOn", () => {
  it("is now for today and the same time of day for an earlier day", () => {
    expect(momentOn("2026-11-12", now)).toBe(now.toISOString());
    const earlier = new Date(momentOn("2026-11-01", now));
    expect([
      earlier.getDate(),
      earlier.getHours(),
      earlier.getMinutes(),
    ]).toEqual([1, 14, 30]);
  });
});

describe("isWithin", () => {
  it("allows open ends", () => {
    expect(isWithin("2026-11-13", null, "2026-11-12")).toBe(false);
    expect(isWithin("2020-01-01", null, "2026-11-12")).toBe(true);
    expect(isWithin("2020-01-01", "2025-01-01", null)).toBe(false);
  });
});
