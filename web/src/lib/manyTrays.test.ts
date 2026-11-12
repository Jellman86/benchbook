import { describe, expect, it } from "vitest";
import { manyTraysResult } from "./manyTrays";
import type { Outcome } from "./work";

const stored: Outcome = { kind: "stored", eventId: "e" };
const queued: Outcome = { kind: "queued", eventId: "e" };
const refused: Outcome = { kind: "refused", problem: "BelowZero" };

describe("manyTraysResult", () => {
  it("says how many were done", () => {
    expect(
      manyTraysResult((n) => `Marked ${n} ready`, [stored, stored]),
    ).toEqual({ message: "Marked 2 trays ready.", allDone: true });
  });

  it("says what is waiting and what failed", () => {
    expect(
      manyTraysResult((n) => `Moved ${n}`, [stored, queued, refused]).message,
    ).toBe(
      "Moved 2 trays; 1 waiting on this device for a signal; 1 tray couldn't be done: open them to see why.",
    );
  });
});
