import type { Schemas } from "./types";

export type SownMeasure = Schemas["SownMeasure"];
export type PacketShare = Schemas["PacketShare"];

/** The ways seed can be measured when sown, as staff choose them. */
export const sowingChoices: { measure: SownMeasure; label: string }[] = [
  { measure: "Counted", label: "I counted them" },
  { measure: "About", label: "About…" },
  { measure: "Grams", label: "By weight" },
  { measure: "Packet", label: "A packet" },
  { measure: "Unknown", label: "Don't know" },
];

export const packetShares: { share: PacketShare; label: string }[] = [
  { share: "Whole", label: "Whole packet" },
  { share: "Half", label: "Half" },
  { share: "Part", label: "Part" },
];

/** Whether seed measured this way starts the tray with a number of plants. */
export const startsCounted = (measure: SownMeasure | null | undefined) =>
  !measure || measure === "Counted";

/** How seed was sown, in words: "about 200 seeds", "0.5 g", "a half packet (approx. 250 seeds)". */
export function sowingWords(sowing: {
  sownAs?: SownMeasure | null;
  sownAmount?: number | string | null;
  packetShare?: PacketShare | null;
  packetSays?: string | null;
}): string {
  const amount = Number(sowing.sownAmount ?? 0);

  switch (sowing.sownAs) {
    case "About":
      return `about ${amount} seeds`;
    case "Grams":
      return `${amount} g`;
    case "Packet": {
      const share =
        sowing.packetShare === "Whole"
          ? "a whole packet"
          : sowing.packetShare === "Half"
            ? "half a packet"
            : "part of a packet";
      return sowing.packetSays ? `${share} (${sowing.packetSays})` : share;
    }
    case "Unknown":
      return "not known";
    default:
      return "";
  }
}

/** How many of a batch are in a tray, or that nobody has counted them yet. */
export const lineAmount = (line: {
  quantity: number | string;
  notCounted?: boolean;
}) => (line.notCounted ? "Not counted yet:" : String(line.quantity));
