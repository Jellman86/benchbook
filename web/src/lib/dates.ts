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

/** A moment as people say it: "today at 14:25", "yesterday at 02:30" or "Sun 8 Nov at 09:00". */
export function momentLabel(moment: string, now = new Date()): string {
  const value = new Date(moment);

  if (Number.isNaN(value.getTime())) {
    return moment;
  }

  const time = value.toLocaleTimeString("en-GB", {
    hour: "2-digit",
    minute: "2-digit",
  });
  const day = (date: Date) =>
    `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
  const yesterday = new Date(now);
  yesterday.setDate(now.getDate() - 1);

  if (day(value) === day(now)) {
    return `today at ${time}`;
  }

  if (day(value) === day(yesterday)) {
    return `yesterday at ${time}`;
  }

  return `${dayLabel(day(value), now)} at ${time}`;
}
