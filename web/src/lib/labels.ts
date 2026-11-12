import type { Schemas } from "./types";

export type LabelKind = Schemas["LabelKind"];
export type LabelStyle = Schemas["LabelStyle"];

type Switch = keyof Pick<
  LabelStyle,
  "showLogo" | "showNurseryName" | "showBotanical" | "showPrice" | "showCare"
>;

export type LabelSize = { width: number; height: number };

/**
 * Each kind of label, what it's for, the roll sizes that suit it and the switches that make sense
 * on it. The sizes are stock that is easy to buy in weatherproof polypropylene, and the first is
 * the one GrowLog starts with. None is wider than 100 mm, because a desktop label printer prints
 * about 104 mm wide at most.
 */
export const kindsOfLabel: {
  kind: LabelKind;
  label: string;
  use: string;
  sizes: LabelSize[];
  switches: { key: Switch; label: string }[];
}[] = [
  {
    kind: "PlantLabel",
    label: "Plant label",
    use: "Sticks on the pot, for customers: the name, and the price if you want it.",
    sizes: [
      { width: 50, height: 25 },
      { width: 57, height: 32 },
      { width: 76, height: 51 },
      { width: 100, height: 50 },
    ],
    switches: [
      { key: "showBotanical", label: "Latin name" },
      { key: "showPrice", label: "Price" },
      { key: "showCare", label: "Care (sun, water, hardiness)" },
      { key: "showLogo", label: "Logo" },
      { key: "showNurseryName", label: "Nursery name" },
    ],
  },
  {
    kind: "BenchCard",
    label: "Bench card",
    use: "Stands on the sales bench, read from a step away: how to grow it and every pot size's price.",
    sizes: [
      { width: 100, height: 150 },
      { width: 100, height: 100 },
      { width: 100, height: 75 },
      { width: 100, height: 50 },
    ],
    switches: [
      { key: "showBotanical", label: "Latin name" },
      { key: "showPrice", label: "Prices" },
      { key: "showCare", label: "Care (sun, water, hardiness)" },
      { key: "showLogo", label: "Logo" },
      { key: "showNurseryName", label: "Nursery name" },
    ],
  },
  {
    kind: "ContainerLabel",
    label: "Tray label",
    use: "Goes on a tray or pot group, to scan when recording work.",
    sizes: [
      { width: 50, height: 25 },
      { width: 57, height: 32 },
      { width: 76, height: 51 },
      { width: 100, height: 50 },
    ],
    switches: [
      { key: "showBotanical", label: "Latin name" },
      { key: "showLogo", label: "Logo" },
      { key: "showNurseryName", label: "Nursery name" },
    ],
  },
  {
    kind: "PlaceLabel",
    label: "Place label",
    use: "Goes on a bench or bed, to scan when moving trays. The tall size is a sign you can read from the path.",
    sizes: [
      { width: 100, height: 150 },
      { width: 100, height: 50 },
      { width: 76, height: 51 },
      { width: 50, height: 25 },
    ],
    switches: [
      { key: "showLogo", label: "Logo" },
      { key: "showNurseryName", label: "Nursery name" },
    ],
  },
  {
    kind: "PlantPassport",
    label: "Plant passport",
    use: "A UK plant passport the nursery issues: parts A to D in their own border, kept apart from everything else on the plants.",
    sizes: [
      { width: 50, height: 25 },
      { width: 57, height: 32 },
      { width: 76, height: 51 },
    ],
    switches: [],
  },
];

/** The text sizes, in the words the owner sees. */
export const textSizes: { layout: LabelStyle["layout"]; label: string }[] = [
  { layout: "Compact", label: "Small" },
  { layout: "Standard", label: "Medium" },
  { layout: "Large", label: "Large" },
];

export const smallestSize = 20;
export const largestSize = 300;

/** A desktop label printer with a 4 inch head prints about this wide and no wider. */
export const widestPrint = 104;

export function sizeLabel(size: LabelSize): string {
  return `${size.width} × ${size.height} mm`;
}

/** What is wrong with a size, or worth knowing about it, in words for the owner. */
export function sizeAdvice(size: LabelSize): {
  problem: string;
  warning: string;
} {
  const outside = (value: number) =>
    !Number.isFinite(value) || value < smallestSize || value > largestSize;

  if (outside(size.width) || outside(size.height)) {
    return {
      problem: `Width and height must each be between ${smallestSize} and ${largestSize} mm.`,
      warning: "",
    };
  }

  return {
    problem: "",
    warning:
      size.width > widestPrint
        ? `Most desktop label printers print ${widestPrint} mm wide at most, so the edges of a ${size.width} mm label would be cut off.`
        : "",
  };
}

/** Adds how many copies to print to a label's address. */
export function withCopies(href: string, copies: number): string {
  return copies > 1
    ? `${href}${href.includes("?") ? "&" : "?"}copies=${copies}`
    : href;
}
