import type { Schemas } from "./types";

export type SheetLayout = Schemas["SheetLayout"];

const sheetKey = "growlog.labelSheet";

/** The sheet this device used last, so the usual one is already chosen. */
export function rememberedSheet(): string | null {
  try {
    return localStorage.getItem(sheetKey);
  } catch {
    return null;
  }
}

export function rememberSheet(id: string) {
  try {
    localStorage.setItem(sheetKey, id);
  } catch {
    // Without storage, the first sheet is offered next time.
  }
}

/** How many columns a sheet has, from its label width: two, three or five across an A4 page. */
export function columnsOf(sheet: Pick<SheetLayout, "widthMm">): number {
  const width = Number(sheet.widthMm);
  return width > 90 ? 2 : width > 50 ? 3 : 5;
}

/** How many pages of stickers so many labels take from a given starting sticker. */
export function sheetsNeeded(
  labels: number,
  perSheet: number,
  startAt: number,
): number {
  if (labels <= 0 || perSheet <= 0) {
    return 0;
  }

  return Math.ceil((labels + Math.max(0, startAt - 1)) / perSheet);
}
