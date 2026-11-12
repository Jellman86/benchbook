import type { Schemas } from "./types";

export type ImportField = Schemas["ImportField"];

/**
 * A CSV file's text. Excel on Windows saves an ordinary "CSV" in its own Windows-1252 encoding, not UTF-8, so
 * "×" and accents would come out as nonsense: a file that isn't valid UTF-8 is read as Windows-1252 instead.
 */
export function decodeSpreadsheet(bytes: ArrayBuffer): string {
  try {
    return new TextDecoder("utf-8", { fatal: true }).decode(bytes);
  } catch {
    return new TextDecoder("windows-1252").decode(bytes);
  }
}

/**
 * The rows of a spreadsheet saved as CSV, each a list of cells. Handles quoted cells with commas or line breaks
 * in them, Excel's byte-order mark, and semicolon or tab separated files.
 */
export function readRows(text: string): string[][] {
  const body = text.replace(/^\uFEFF/, "");
  const firstLine = body.split(/\r?\n/, 1)[0] ?? "";
  const separator = [",", ";", "\t"].reduce((best, next) =>
    firstLine.split(next).length > firstLine.split(best).length ? next : best,
  );
  const rows: string[][] = [];
  let row: string[] = [];
  let cell = "";
  let quoted = false;

  for (let index = 0; index < body.length; index++) {
    const char = body[index];

    if (quoted) {
      if (char === '"' && body[index + 1] === '"') {
        cell += '"';
        index++;
      } else if (char === '"') {
        quoted = false;
      } else {
        cell += char;
      }
    } else if (char === '"' && cell === "") {
      quoted = true;
    } else if (char === separator) {
      row.push(cell);
      cell = "";
    } else if (char === "\n" || char === "\r") {
      if (char === "\r" && body[index + 1] === "\n") {
        index++;
      }
      row.push(cell);
      rows.push(row);
      row = [];
      cell = "";
    } else {
      cell += char;
    }
  }

  if (cell !== "" || row.length > 0) {
    row.push(cell);
    rows.push(row);
  }

  return rows;
}

const plain = (text: string) => text.toLowerCase().replace(/[^a-z0-9]/g, "");

/** Which spreadsheet column each field takes, matched on its name or a usual other name; -1 for none. */
export function matchColumns(
  header: string[],
  fields: ImportField[],
): Record<string, number> {
  const columns = header.map(plain);
  const taken = new Set<number>();
  const matched: Record<string, number> = {};

  for (const field of fields) {
    const names = [field.label, field.key, ...field.aliases].map(plain);
    const column = columns.findIndex(
      (name, index) => !taken.has(index) && names.includes(name),
    );
    matched[field.key] = column;

    if (column >= 0) {
      taken.add(column);
    }
  }

  return matched;
}

/** The rows to send: each with its spreadsheet row number (the header is row 1) and a value for each matched field. */
export function mappedRows(
  rows: string[][],
  matched: Record<string, number>,
): { row: number; values: Record<string, string> }[] {
  return rows
    .map((cells, index) => ({
      row: index + 2,
      values: Object.fromEntries(
        Object.entries(matched)
          .filter(([, column]) => column >= 0)
          .map(([key, column]) => [key, (cells[column] ?? "").trim()]),
      ),
    }))
    .filter((row) => Object.values(row.values).some((value) => value !== ""));
}
