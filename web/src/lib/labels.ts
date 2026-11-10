import type { Schemas } from "./types";

export type LabelKind = Schemas["LabelKind"];
export type LabelStyle = Schemas["LabelStyle"];

type Switch = keyof Pick<
  LabelStyle,
  "showLogo" | "showNurseryName" | "showBotanical" | "showPrice" | "showCare"
>;

/** Each kind of label, what it's for, and the switches that make sense on it. */
export const kindsOfLabel: {
  kind: LabelKind;
  label: string;
  use: string;
  switches: { key: Switch; label: string }[];
}[] = [
  {
    kind: "PlantLabel",
    label: "Plant label",
    use: "Goes in the pot or on it, for customers.",
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
    use: "Stands on the sales bench, read from a distance.",
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
    switches: [
      { key: "showBotanical", label: "Latin name" },
      { key: "showLogo", label: "Logo" },
      { key: "showNurseryName", label: "Nursery name" },
    ],
  },
  {
    kind: "PlaceLabel",
    label: "Place label",
    use: "Goes on a bench or bed, to scan when moving trays.",
    switches: [
      { key: "showLogo", label: "Logo" },
      { key: "showNurseryName", label: "Nursery name" },
    ],
  },
];

/** Common label roll sizes, width by height, in millimetres. */
export const sizePresets: { width: number; height: number }[] = [
  { width: 50, height: 25 },
  { width: 57, height: 32 },
  { width: 70, height: 25 },
  { width: 70, height: 40 },
  { width: 100, height: 50 },
  { width: 100, height: 150 },
  { width: 105, height: 148 },
];

export function sizeLabel(size: { width: number; height: number }): string {
  return `${size.width} × ${size.height} mm`;
}

/** Adds how many copies to print to a label's address. */
export function withCopies(href: string, copies: number): string {
  return copies > 1
    ? `${href}${href.includes("?") ? "&" : "?"}copies=${copies}`
    : href;
}
