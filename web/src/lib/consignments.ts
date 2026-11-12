import type { Schemas } from "./types";

export type ConsignmentKind = Schemas["ConsignmentKind"];

/** The four ways plants leave the nursery, each with whether a plant passport goes with them. */
export const kindChoices: {
  kind: ConsignmentKind;
  label: string;
  needed: boolean;
  why: string;
}[] = [
  {
    kind: "TradeSale",
    label: "To a business",
    needed: true,
    why: "Plants moved to another business need a UK plant passport.",
  },
  {
    kind: "DistanceSale",
    label: "Posted or couriered",
    needed: true,
    why: "Plants sold at a distance (post, courier or online) need a UK plant passport, even to a home gardener.",
  },
  {
    kind: "InPerson",
    label: "Sold in person",
    needed: false,
    why: "Plants sold in person to someone who will grow them themselves need no passport.",
  },
  {
    kind: "OwnSite",
    label: "To our other site",
    needed: false,
    why: "Moving plants between your own sites usually needs no passport. If the sites are far apart, check with your plant health inspector.",
  },
];

export const kindLabel = (kind: ConsignmentKind) =>
  kindChoices.find((choice) => choice.kind === kind)?.label ?? kind;

export type DraftLine = { tray: string; quantity: number };

/**
 * Trays asked for more plants, across every package, than they hold. The key of a tray is its
 * container and batch together, as one tray can hold more than one batch.
 */
export function overdrawn(
  packages: DraftLine[][],
  holds: (tray: string) => number,
): string[] {
  const asked = new Map<string, number>();

  for (const line of packages.flat()) {
    if (line.tray) {
      asked.set(line.tray, (asked.get(line.tray) ?? 0) + line.quantity);
    }
  }

  return [...asked]
    .filter(([tray, quantity]) => quantity > holds(tray))
    .map(([tray]) => tray);
}

/** Whether a consignment's packages are ready to record: each has a line, each line a tray and at least one plant. */
export const packagesReady = (packages: DraftLine[][]) =>
  packages.length > 0 &&
  packages.every(
    (lines) =>
      lines.length > 0 &&
      lines.every(
        (line) =>
          line.tray && Number.isInteger(line.quantity) && line.quantity >= 1,
      ),
  );

/** Why a consignment or a package passport wasn't recorded, in words. */
export function consignmentProblem(status: number, problem?: string): string {
  switch (problem) {
    case "TooMany":
      return "A tray doesn't hold that many now. Check the numbers.";
    case "NotExamined":
      return "Examine the plants in this package first, and tick to say so.";
    case "CannotIssue":
      return "One of the trays in this package can't have a passport yet. Open the tray's plant passport to see what's missing, such as the nursery's registration number.";
    default:
      return status === 400
        ? "Say who the plants are going to, and put at least one tray in each package."
        : "That couldn't be recorded. Please try again.";
  }
}

/** A package's passport is still to issue when one is needed and none has been. */
export const passportsLeft = (consignment: {
  passportNeeded: boolean;
  packages: number | string;
  packagesPassported: number | string;
}) =>
  consignment.passportNeeded
    ? Number(consignment.packages) - Number(consignment.packagesPassported)
    : 0;
