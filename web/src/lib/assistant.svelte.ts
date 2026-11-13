import { api } from "../api/client";
import { isOn } from "./features.svelte";
import type { Schemas } from "./types";

// Whether this person can use Ask GrowLog, and whether its model reads photos, for screens to offer it.
/** What a question is asked from: a tray, a plant or one of a batch's photos, with words to show for it. */
export type AskAbout = {
  title: string;
  trayLabel?: string | null;
  plantId?: string | null;
  photoId?: string | null;
};
export type AskSource = Schemas["AskSource"];

export const assistant = $state({
  canAsk: false,
  readsImages: false,
  // What this person's questions have cost the nursery, in US dollars as OpenRouter charges.
  spentThisMonth: null as number | null,
  spentInAll: null as number | null,
});

/** A cost in US dollars, as OpenRouter charges, in words a person can take in at a glance. */
export function dollars(amount: number): string {
  if (amount <= 0) {
    return "$0.00";
  }

  return amount < 0.01 ? "under 1 cent" : `$${amount.toFixed(2)}`;
}

export async function loadAssistant() {
  if (!isOn("Assistant")) {
    assistant.canAsk = false;
    return;
  }

  try {
    const { data } = await api.GET("/api/assistant/status");
    assistant.canAsk = !!data?.canAsk;
    assistant.readsImages = !!data?.readsImages;
    assistant.spentThisMonth =
      data?.spent != null ? Number(data.spent.thisMonth) : null;
    assistant.spentInAll =
      data?.spent != null ? Number(data.spent.inAll) : null;
  } catch {
    // Offline: it can't be asked anyway.
    assistant.canAsk = false;
  }
}

/** An answer as paragraphs and lists, read as plain text: nothing from the model is ever treated as markup. */
export function answerBlocks(
  text: string,
): ({ kind: "p"; text: string } | { kind: "list"; items: string[] })[] {
  const blocks: (
    { kind: "p"; text: string } | { kind: "list"; items: string[] }
  )[] = [];

  for (const chunk of text.replace(/\r/g, "").split(/\n{2,}/)) {
    const lines = chunk
      .split("\n")
      .map((line) => line.replace(/\*\*(.+?)\*\*/g, "$1").trimEnd())
      .filter((line) => line.trim().length > 0);
    const listed = lines.filter((line) => /^\s*([-*•]|\d+[.)])\s+/.test(line));

    if (lines.length > 0 && listed.length === lines.length) {
      blocks.push({
        kind: "list",
        items: lines.map((line) => line.replace(/^\s*([-*•]|\d+[.)])\s+/, "")),
      });
    } else if (lines.length > 0) {
      blocks.push({
        kind: "p",
        text: lines.join("\n").replace(/^#+\s*/gm, ""),
      });
    }
  }

  return blocks;
}
