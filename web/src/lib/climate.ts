import { isoDay } from "./days";

export type HourPoint = {
  at: number;
  value: number;
  low?: number;
  high?: number;
};

/** Four to six round numbers covering the values, for the gridlines. */
export function niceTicks(lowest: number, highest: number): number[] {
  const span = Math.max(highest - lowest, 1);
  const rough = span / 4;
  const magnitude = 10 ** Math.floor(Math.log10(rough));
  const step =
    [1, 2, 2.5, 5, 10].map((m) => m * magnitude).find((s) => s >= rough) ??
    10 * magnitude;
  const ticks = [Math.floor(lowest / step) * step];

  while (ticks[ticks.length - 1] < highest) {
    ticks.push(Math.round((ticks[ticks.length - 1] + step) * 100) / 100);
  }

  return ticks.length > 1 ? ticks : [ticks[0], ticks[0] + step];
}

/** Runs of readings with no hour missing for longer than the gap, so a sensor that went quiet shows as a break. */
export function runs(points: HourPoint[], gapHours = 2): HourPoint[][] {
  const result: HourPoint[][] = [];

  for (const point of points) {
    const run = result.at(-1);
    const last = run?.at(-1);

    if (run && last && point.at - last.at <= gapHours * 3_600_000) {
      run.push(point);
    } else {
      result.push([point]);
    }
  }

  return result;
}

export type ClimateDay = {
  day: string;
  lowest: number | null;
  highest: number | null;
  humidity: number | null;
};

type Hour = {
  hour: string;
  temperature?: number | string | null;
  lowestTemperature?: number | string | null;
  highestTemperature?: number | string | null;
  humidity?: number | string | null;
};

const known = (value: number | string | null | undefined) =>
  value == null ? null : Number(value);

/** Each day's lowest and highest temperature and its average humidity, newest first, for the table view. */
export function climateDays(hours: Hour[]): ClimateDay[] {
  const days = new Map<string, Hour[]>();

  for (const hour of hours) {
    const day = isoDay(new Date(hour.hour));
    days.set(day, [...(days.get(day) ?? []), hour]);
  }

  return [...days]
    .map(([day, group]) => {
      const lows = group
        .map((hour) => known(hour.lowestTemperature))
        .filter((value) => value !== null);
      const highs = group
        .map((hour) => known(hour.highestTemperature))
        .filter((value) => value !== null);
      const damp = group
        .map((hour) => known(hour.humidity))
        .filter((value) => value !== null);

      return {
        day,
        lowest: lows.length ? Math.min(...lows) : null,
        highest: highs.length ? Math.max(...highs) : null,
        humidity: damp.length
          ? Math.round(
              damp.reduce((sum, value) => sum + value, 0) / damp.length,
            )
          : null,
      };
    })
    .sort((a, b) => b.day.localeCompare(a.day));
}

/** A reading's age in words, and whether it's old enough that the sensor may have stopped. */
export function readingAge(
  takenAt: string,
  now: Date,
): { words: string; stale: boolean } {
  const minutes = Math.max(
    0,
    Math.round((now.getTime() - new Date(takenAt).getTime()) / 60_000),
  );
  const words =
    minutes < 2
      ? "just now"
      : minutes < 60
        ? `${minutes} minutes ago`
        : minutes < 120
          ? "an hour ago"
          : minutes < 48 * 60
            ? `${Math.round(minutes / 60)} hours ago`
            : `${Math.round(minutes / 1440)} days ago`;

  return { words, stale: minutes > 60 };
}

export const degrees = (value: number | string | null | undefined) =>
  value == null ? "–" : `${Number(value).toFixed(1)}°C`;

export const percent = (value: number | string | null | undefined) =>
  value == null ? "–" : `${Math.round(Number(value))}%`;
