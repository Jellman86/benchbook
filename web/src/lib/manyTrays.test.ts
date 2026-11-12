import { describe, expect, it } from "vitest";
import { manyTraysResult } from "./manyTrays";
import type { Outcome } from "./work";

const stored: Outcome = { kind: "stored", eventId: "e" };
const queued: Outcome = { kind: "queued", eventId: "e" };
const refused: Outcome = { kind: "refused", problem: "BelowZero" };
const on = (labelCode: string, outcome: Outcome) => ({
  id: `id-${labelCode}`,
  labelCode,
  outcome,
});

describe("manyTraysResult", () => {
  it("says how many were done", () => {
    expect(
      manyTraysResult(
        (n) => `Marked ${n} ready`,
        [on("C1", stored), on("C2", stored)],
      ),
    ).toEqual({ message: "Marked 2 trays ready.", allDone: true, failed: [] });
  });

  it("names the trays that failed and keeps only those", () => {
    const result = manyTraysResult(
      (n) => `Moved ${n}`,
      [
        on("C1", stored),
        on("C2", queued),
        on("C3", refused),
        on("C4", refused),
      ],
    );
    expect(result.message).toBe(
      "Moved 2 trays; 1 waiting on this device for a signal; C3 and C4 couldn't be done: open them to see why. Only those are still chosen.",
    );
    expect(result.failed).toEqual(["id-C3", "id-C4"]);
  });
});
