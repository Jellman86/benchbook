import { isoDay } from "./days";

/** How long a chosen earlier day is remembered for a tray: long enough to write up several things in a row. */
const remembered = 30 * 60_000;

const chosen = new Map<string, { day: string; at: number }>();

/** The day work on a tray is being written up for, if one was chosen in the last half hour; otherwise today. */
export function workDayFor(label: string, now = new Date()): string {
  const kept = chosen.get(label);
  return kept && now.getTime() - kept.at < remembered ? kept.day : isoDay(now);
}

/** Remembers the day chosen for a tray, or forgets it when it's today again. */
export function chooseWorkDay(label: string, day: string, now = new Date()) {
  if (day === isoDay(now)) {
    chosen.delete(label);
  } else {
    chosen.set(label, { day, at: now.getTime() });
  }
}
