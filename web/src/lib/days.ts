import { dayLabel } from "./dates";

/** A calendar day as the browser's date inputs and the API write it, in local time: 2026-11-12. */
export function isoDay(date: Date): string {
  return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
}

function parse(day: string): Date {
  const [year, month, date] = day.split("-").map(Number);
  return new Date(year, month - 1, date);
}

export function addDays(day: string, days: number): string {
  const value = parse(day);
  value.setDate(value.getDate() + days);
  return isoDay(value);
}

/** The first day of the month a day falls in, moved by some months. */
export function addMonths(day: string, months: number): string {
  const value = parse(day);
  return isoDay(new Date(value.getFullYear(), value.getMonth() + months, 1));
}

/** "November 2026". */
export function monthName(day: string): string {
  return parse(day).toLocaleDateString("en-GB", {
    month: "long",
    year: "numeric",
  });
}

/** "Thursday 12 November 2026", for a screen reader. */
export function longDay(day: string): string {
  return parse(day).toLocaleDateString("en-GB", {
    weekday: "long",
    day: "numeric",
    month: "long",
    year: "numeric",
  });
}

/** The days of a month in weeks starting on Monday, with blanks before the 1st. */
export function monthGrid(day: string): (string | null)[] {
  const first = parse(addMonths(day, 0));
  const blanks = (first.getDay() + 6) % 7;
  const length = new Date(
    first.getFullYear(),
    first.getMonth() + 1,
    0,
  ).getDate();

  return [
    ...Array.from({ length: blanks }, () => null),
    ...Array.from({ length }, (_, index) =>
      isoDay(new Date(first.getFullYear(), first.getMonth(), index + 1)),
    ),
  ];
}

/** "Today", "Yesterday" or "Tue 10 Nov". */
export function dayWords(day: string, now = new Date()): string {
  const today = isoDay(now);

  if (day === today) {
    return "Today";
  }

  if (day === addDays(today, -1)) {
    return "Yesterday";
  }

  return dayLabel(day, now);
}

/** "today", "yesterday" or "on Tue 10 Nov", to follow a word such as "Sown". */
export function onDay(day: string, now = new Date()): string {
  const words = dayWords(day, now);
  return words === "Today" || words === "Yesterday"
    ? words.toLowerCase()
    : `on ${words}`;
}

/**
 * The moment to record for work done on a chosen day: now if it's today, otherwise the same time of day
 * on that day, so work recorded together keeps its order.
 */
export function momentOn(day: string, now = new Date()): string {
  if (day === isoDay(now)) {
    return now.toISOString();
  }

  const value = parse(day);
  value.setHours(now.getHours(), now.getMinutes(), now.getSeconds());
  return value.toISOString();
}

export const isWithin = (day: string, min: string | null, max: string | null) =>
  (!min || day >= min) && (!max || day <= max);
