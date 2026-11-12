import { isRecorded, type Outcome } from "./work";

const trays = (count: number) => `${count} tray${count === 1 ? "" : "s"}`;

/**
 * What happened when one action was recorded on several trays, in a sentence. Work waiting for a signal counts as
 * done, and says so.
 */
export function manyTraysResult(
  done: (trays: string) => string,
  outcomes: Outcome[],
): { message: string; allDone: boolean } {
  const recorded = outcomes.filter((outcome) => isRecorded(outcome)).length;
  const waiting = outcomes.filter(
    (outcome) => outcome.kind === "queued",
  ).length;
  const failed = outcomes.length - recorded;

  const parts = [
    recorded > 0 ? done(trays(recorded)) : "",
    waiting > 0
      ? `${waiting === recorded ? "all" : waiting} waiting on this device for a signal`
      : "",
    failed > 0 ? `${trays(failed)} couldn't be done: open them to see why` : "",
  ].filter(Boolean);

  return { message: `${parts.join("; ")}.`, allDone: failed === 0 };
}
