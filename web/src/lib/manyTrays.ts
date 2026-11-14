import { isRecorded, type Outcome } from "./work";

const trays = (count: number) => `${count} tray${count === 1 ? "" : "s"}`;

const listed = (labels: string[]) =>
  labels.length > 1
    ? `${labels.slice(0, -1).join(", ")} and ${labels.at(-1)}`
    : (labels[0] ?? "");

/**
 * What happened when one action was recorded on several trays, in a sentence. Work waiting for a signal counts as
 * done, and says so. Trays that couldn't be done are named, and given back so they alone stay chosen: pressing
 * again then can't repeat the work on the trays that took it.
 */
export function manyTraysResult(
  done: (trays: string, count: number) => string,
  results: { id: string; labelCode: string; outcome: Outcome }[],
): { message: string; allDone: boolean; failed: string[] } {
  const recorded = results.filter((result) =>
    isRecorded(result.outcome),
  ).length;
  const waiting = results.filter(
    (result) => result.outcome.kind === "queued",
  ).length;
  const failed = results.filter((result) => !isRecorded(result.outcome));

  const parts = [
    recorded > 0 ? `${done(trays(recorded), recorded)}.` : "",
    waiting === 0
      ? ""
      : waiting < recorded
        ? `${waiting} of them ${waiting === 1 ? "is" : "are"} waiting on this device for a signal.`
        : waiting === 1
          ? "It is waiting on this device for a signal."
          : "They are all waiting on this device for a signal.",
    failed.length > 0
      ? `${listed(failed.map((result) => result.labelCode))} couldn't be done, so open ${failed.length === 1 ? "it" : "them"} to see why. Only ${failed.length === 1 ? "that one is" : "those are"} still chosen.`
      : "",
  ].filter(Boolean);

  return {
    message: parts.join(" "),
    allDone: failed.length === 0,
    failed: failed.map((result) => result.id),
  };
}
