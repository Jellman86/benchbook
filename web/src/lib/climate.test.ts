import { describe, expect, it } from "vitest";
import { climateDays, niceTicks, readingAge, runs } from "./climate";

describe("climate", () => {
  it("chooses round gridlines spanning the values", () => {
    expect(niceTicks(6.2, 24.8)).toEqual([5, 10, 15, 20, 25]);
    expect(niceTicks(55, 92)).toEqual([50, 60, 70, 80, 90, 100]);
  });

  it("breaks the line where the sensor went quiet", () => {
    const hour = 3_600_000;
    const points = [0, 1, 2, 6, 7].map((n) => ({ at: n * hour, value: n }));
    expect(runs(points).map((run) => run.length)).toEqual([3, 2]);
  });

  it("sums up each day, newest first", () => {
    const days = climateDays([
      {
        hour: "2026-11-10T09:00:00Z",
        lowestTemperature: 8,
        highestTemperature: 12,
        humidity: 80,
      },
      {
        hour: "2026-11-10T14:00:00Z",
        lowestTemperature: 15,
        highestTemperature: 21.5,
        humidity: 60,
      },
      {
        hour: "2026-11-11T09:00:00Z",
        lowestTemperature: 9,
        highestTemperature: 10,
        humidity: null,
      },
    ]);
    expect(days).toEqual([
      { day: "2026-11-11", lowest: 9, highest: 10, humidity: null },
      { day: "2026-11-10", lowest: 8, highest: 21.5, humidity: 70 },
    ]);
  });

  it("says how old a reading is and when it's stale", () => {
    const now = new Date("2026-11-12T12:00:00Z");
    expect(readingAge("2026-11-12T11:55:00Z", now)).toEqual({
      words: "5 minutes ago",
      stale: false,
    });
    expect(readingAge("2026-11-12T09:00:00Z", now)).toEqual({
      words: "3 hours ago",
      stale: true,
    });
  });
});
