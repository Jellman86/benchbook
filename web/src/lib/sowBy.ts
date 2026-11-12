/** The last day of a month, as a date the API takes: a packet's "sow by 12/2027" runs to the end of December. */
export function monthEnd(year: number, month: number): string {
  const last = new Date(year, month, 0).getDate();
  return `${year}-${String(month).padStart(2, "0")}-${String(last).padStart(2, "0")}`;
}

/** The month and year of a stored sow-by date, or null when there isn't one. */
export function sowByParts(
  value: string | null | undefined,
): { year: number; month: number } | null {
  const [year, month] = (value ?? "").split("-").map(Number);
  return year && month ? { year, month } : null;
}

/** "Dec 2027", as a packet would say it. */
export function sowByWords(value: string | null | undefined): string {
  const parts = sowByParts(value);
  return parts
    ? new Date(parts.year, parts.month - 1, 1).toLocaleDateString("en-GB", {
        month: "short",
        year: "numeric",
      })
    : "";
}

/** Whether a packet's date has passed, so a screen can say so. */
export const sowByPassed = (
  value: string | null | undefined,
  now = new Date(),
) => !!value && value < now.toISOString().slice(0, 10);
