import { describe, expect, it } from "vitest";
import { dayLabel, momentLabel } from "./dates";

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

describe("moments", () => {
  const now = new Date(2026, 10, 10, 15, 0);
  const at = (day: number, hour: number, minute: number) =>
    new Date(2026, 10, day, hour, minute).toISOString();

  it("says today and yesterday", () => {
    expect(momentLabel(at(10, 14, 25), now)).toBe("today at 14:25");
    expect(momentLabel(at(9, 2, 30), now)).toBe("yesterday at 02:30");
  });

  it("names older days", () => {
    expect(momentLabel(at(8, 9, 0), now)).toBe("Sun 8 Nov at 09:00");
  });

  it("leaves anything else alone", () => {
    expect(momentLabel("never", now)).toBe("never");
  });
});
