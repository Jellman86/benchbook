/** A calendar date such as 2026-11-08 as people say it: "Sun 8 Nov", with the year if it isn't this year. */
export function dayLabel(date: string, now = new Date()): string {
  const [year, month, day] = date.split("-").map(Number);

  if (!year || !month || !day) {
    return date;
  }

  const value = new Date(year, month - 1, day);
  const part = (options: Intl.DateTimeFormatOptions) =>
    value.toLocaleDateString("en-GB", options);
  const words = `${part({ weekday: "short" })} ${day} ${part({ month: "short" })}`;

  return year === now.getFullYear() ? words : `${words} ${year}`;
}
