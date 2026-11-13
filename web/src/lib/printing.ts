import { api } from "../api/client";
import type { Schemas } from "./types";

export type PrinterDetails = Schemas["PrinterDetails"];
export type LabelKind = Schemas["LabelKind"];

/** What a label link prints, so the same label can go straight to a printer instead. */
export function labelOf(
  href: string,
): { kind: LabelKind; target: string; potSizeId: string | null } | null {
  const url = new URL(href, "https://growlog.invalid");
  const [, , , what, target] = url.pathname.split("/");
  const kinds: Record<string, LabelKind> = {
    containers: "ContainerLabel",
    plants: "PlantLabel",
    "bench-cards": "BenchCard",
    places: "PlaceLabel",
    passports: "PlantPassport",
  };
  const kind = what ? kinds[what] : undefined;

  return kind && target
    ? {
        kind,
        target: decodeURIComponent(target),
        potSizeId: url.searchParams.get("potSizeId"),
      }
    : null;
}

let known: Promise<PrinterDetails[]> | null = null;

/** The nursery's printers, asked for once per visit. */
export function printers(): Promise<PrinterDetails[]> {
  known ??= api
    .GET("/api/printers")
    .then(({ data }) => data ?? [])
    .catch(() => []);
  return known;
}

/** After printers change, the next print button asks again. */
export function forgetPrinters() {
  known = null;
}

export const printProblems: Record<string, string> = {
  LabelTooSmall:
    "The label stock is too small for this passport. Choose a larger label.",
  "not-found": "GrowLog couldn't find a printer at that address.",
  "not-allowed":
    "That address belongs to the server itself, so it can't be a printer.",
  unreachable:
    "The printer didn't answer. Check it's switched on, on the network, and the address is right.",
};

export const languages: {
  language: Schemas["PrinterLanguage"];
  label: string;
}[] = [
  { language: "Zpl", label: "ZPL: Zebra, and printers that copy it" },
  { language: "Tspl", label: "TSPL: TSC, and printers that copy it" },
];

export const printableKinds: { kind: LabelKind; label: string }[] = [
  { kind: "PlantLabel", label: "Plant labels" },
  { kind: "ContainerLabel", label: "Tray labels" },
  { kind: "BenchCard", label: "Bench cards" },
  { kind: "PlantPassport", label: "Plant passports" },
  { kind: "PlaceLabel", label: "Place labels" },
];
