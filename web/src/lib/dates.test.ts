import { describe, expect, it } from "vitest";
import { dayLabel } from "./dates";

describe("days", () => {
  const now = new Date(2026, 10, 10);

  it("reads like speech this year", () => {
    expect(dayLabel("2026-11-08", now)).toBe("Sun 8 Nov");
  });

  it("adds the year for other years", () => {
    expect(dayLabel("2025-12-24", now)).toBe("Wed 24 Dec 2025");
  });

  it("leaves anything else alone", () => {
    expect(dayLabel("soon", now)).toBe("soon");
  });
});
