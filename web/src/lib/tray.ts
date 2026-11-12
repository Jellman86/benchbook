import { dayLabel } from "./dates";
import type { Schemas } from "./types";
import { lossReasons } from "./work";

export type TrayAction =
  | "lost"
  | "move"
  | "potOn"
  | "prickOut"
  | "count"
  | "treat"
  | "note"
  | "passports"
  | "germinated"
  | "ready";

type Container = Schemas["ContainerResponse"];
export type HistoryEntry = Schemas["HistoryEntry"];

/** Only seed sown in a tray germinates; cuttings and bought-in plants don't. */
export function canGerminate(container: Container): boolean {
  const stage = container.stage ?? null;
  return (
    container.contents.some((line) => line.source === "SeedLot") &&
    (stage === null || stage === "Sown")
  );
}

/** The one or two things most likely to happen next, shown large. */
export function likelyNext(container: Container): TrayAction[] {
  if (container.contents.length === 0) {
    return ["move"];
  }

  switch (container.stage) {
    case "Sown":
      return canGerminate(container) ? ["germinated", "lost"] : ["lost"];
    case "Germinated":
      return ["prickOut", "lost"];
    case "Cuttings":
    case "BoughtIn":
    case "PrickedOut":
      return ["potOn", "lost"];
    case "PottedOn":
      return ["ready", "potOn"];
    case "Ready":
      return ["move", "count"];
    default:
      return ["lost", "move"];
  }
}

/** Everything else that can be done, in a steady order. */
export function otherActions(container: Container): TrayAction[] {
  const first = likelyNext(container);
  const hasPlants = container.contents.length > 0;
  const all: TrayAction[] = hasPlants
    ? [
        "lost",
        "potOn",
        "prickOut",
        ...(canGerminate(container) ? (["germinated"] as const) : []),
        "ready",
        "count",
        "move",
        "treat",
        "note",
        "passports",
      ]
    : ["move", "treat", "note"];

  return all.filter((action) => !first.includes(action));
}

export const actionLabels: Record<TrayAction, string> = {
  lost: "Lost",
  move: "Move",
  potOn: "Pot on",
  prickOut: "Prick out",
  count: "Count",
  treat: "Treated",
  note: "Note",
  passports: "Plant passports",
  germinated: "Germinated",
  ready: "Ready",
};

/** A short, friendly time: "today", "yesterday", "3 days ago", then the date. */
export function whenWords(date: string, now: Date): string {
  const then = new Date(date);
  const startOfDay = (value: Date) =>
    new Date(value.getFullYear(), value.getMonth(), value.getDate()).getTime();
  const days = Math.round((startOfDay(now) - startOfDay(then)) / 86_400_000);

  if (days <= 0) {
    return "today";
  }

  if (days === 1) {
    return "yesterday";
  }

  if (days < 7) {
    return `${days} days ago`;
  }

  const pad = (value: number) => String(value).padStart(2, "0");
  return dayLabel(
    `${then.getFullYear()}-${pad(then.getMonth() + 1)}-${pad(then.getDate())}`,
    now,
  );
}

/** One line about something that happened to a tray. */
export function describeHistory(entry: HistoryEntry): string {
  const quantity = Math.abs(Number(entry.quantity ?? 0));
  const details = entry.details as Record<string, string> | null;
  const into = entry.potSizeName ? ` into ${entry.potSizeName}` : "";

  switch (entry.type) {
    case "Sown":
      return quantity === 0 ? "Sown, not counted yet" : `Sown: ${quantity}`;
    case "Received":
      return `Arrived: ${quantity}`;
    case "CuttingsTaken":
      return `Cuttings taken: ${quantity}`;
    case "Germinated":
      return "Germinated";
    case "PrickedOut":
      return Number(entry.quantity) > 0
        ? `Pricked out here: ${quantity}${into}`
        : `Pricked out: ${quantity}`;
    case "PottedOn":
      return Number(entry.quantity) > 0
        ? `Potted on here: ${quantity}${into}`
        : `Potted on: ${quantity}`;
    case "Moved":
      return `Moved to ${entry.locationName ?? "another place"}`;
    case "Lost": {
      const reason = lossReasons.find(
        (item) => item.reason === details?.reason,
      )?.label;
      const all = (entry.details as { all?: boolean } | null)?.all;
      const how =
        all === true ? "all of them" : all === false ? "some" : `${quantity}`;
      return reason ? `Lost ${how} (${reason})` : `Lost ${how}`;
    }
    case "Treated":
      return `Treated with ${details?.product ?? "a product"}`;
    case "StockCounted": {
      const counted = (entry.details as { counted?: number } | null)?.counted;
      const change = entry.quantity
        ? ` (${Number(entry.quantity) > 0 ? "+" : "−"}${quantity})`
        : "";
      return counted === undefined
        ? `Counted${change}`
        : `Counted ${counted}${change || ", as expected"}`;
    }
    case "Note":
      return `Note: ${details?.note ?? ""}`;
    case "Ready":
      return "Marked ready";
    case "Examined":
      return details?.forPassport
        ? `Examined for a plant passport: ${details?.findings ?? "no pests or diseases seen"}`
        : `Examined${details?.findings ? `: ${details.findings}` : ""}`;
    default:
      return String(entry.type);
  }
}

/** Roughly how much a pot holds, in litres, from names such as "9cm", "2L" or "500ml". */
export function potVolume(name: string): number | null {
  const found = /([\d.]+)\s*(cm|ml|l|litres?|ltr)\b/iu.exec(name);

  if (!found) {
    return null;
  }

  const size = Number(found[1]);
  const unit = found[2].toLowerCase();

  // A 9cm pot holds about half a litre, and volume grows with the cube of width.
  return unit === "cm"
    ? (size / 9) ** 3 * 0.5
    : unit === "ml"
      ? size / 1000
      : size;
}

/** The pot size one up from the current one, or the first that isn't the current one. */
export function nextPotSize(
  sizes: { id: string; name: string }[],
  currentId: string | null | undefined,
): string {
  const current = sizes.find((size) => size.id === currentId);
  const currentVolume = current ? potVolume(current.name) : null;

  if (currentVolume !== null) {
    const bigger = sizes
      .map((size) => ({ id: size.id, volume: potVolume(size.name) }))
      .filter(
        (size): size is { id: string; volume: number } =>
          size.volume !== null && size.volume > currentVolume,
      )
      .sort((a, b) => a.volume - b.volume)[0];

    if (bigger) {
      return bigger.id;
    }
  }

  return sizes.find((size) => size.id !== currentId)?.id ?? sizes[0]?.id ?? "";
}

/** Half and all of what's there, for one press, saying which is which. */
export function quantityPresets(
  available: number,
): { label: string; value: number }[] {
  const half = Math.floor(available / 2);
  return [
    ...(half > 0 && half < available
      ? [{ label: `Half (${half})`, value: half }]
      : []),
    ...(available > 0
      ? [{ label: `All (${available})`, value: available }]
      : []),
  ];
}
