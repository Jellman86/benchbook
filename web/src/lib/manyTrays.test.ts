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
        (n, count) => `${n} ${count === 1 ? "was" : "were"} marked ready`,
        [on("C1", stored), on("C2", stored)],
      ),
    ).toEqual({
      message: "2 trays were marked ready.",
      allDone: true,
      failed: [],
    });
  });

  it("names the trays that failed and keeps only those", () => {
    const result = manyTraysResult(
      (n) => `${n} were moved`,
      [
        on("C1", stored),
        on("C2", queued),
        on("C3", refused),
        on("C4", refused),
      ],
    );
    expect(result.message).toBe(
      "2 trays were moved. 1 of them is waiting on this device for a signal. C3 and C4 couldn't be done, so open them to see why. Only those are still chosen.",
    );
    expect(result.failed).toEqual(["id-C3", "id-C4"]);
  });
});
