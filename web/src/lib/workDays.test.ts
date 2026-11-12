import { describe, expect, it } from "vitest";
import { chooseWorkDay, workDayFor } from "./workDays";

describe("the day work is written up for", () => {
  it("is remembered for a tray for half an hour, then goes back to today", () => {
    const now = new Date("2026-11-12T10:00:00");
    chooseWorkDay("C000001", "2026-11-09", now);
    expect(workDayFor("C000001", new Date("2026-11-12T10:20:00"))).toBe(
      "2026-11-09",
    );
    expect(workDayFor("C000002", now)).toBe("2026-11-12");
    expect(workDayFor("C000001", new Date("2026-11-12T10:31:00"))).toBe(
      "2026-11-12",
    );
  });

  it("forgets the day when today is chosen again", () => {
    const now = new Date("2026-11-12T10:00:00");
    chooseWorkDay("C000003", "2026-11-10", now);
    chooseWorkDay("C000003", "2026-11-12", now);
    expect(workDayFor("C000003", now)).toBe("2026-11-12");
  });
});
