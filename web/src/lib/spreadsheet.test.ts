import { describe, expect, it } from "vitest";
import {
  decodeSpreadsheet,
  mappedRows,
  matchColumns,
  readRows,
  type ImportField,
} from "./spreadsheet";

const field = (key: string, label: string, aliases: string[] = []) =>
  ({
    key,
    label,
    aliases,
    type: "Text",
    required: false,
    longest: 200,
  }) as ImportField;

describe("reading a spreadsheet", () => {
  it("reads quoted cells, commas and line breaks inside them", () => {
    expect(
      readRows(
        '﻿Name,Address\r\n"Hilltop, Ltd","1 High St\nBakewell"\r\n"Say ""hi""",x\r\n',
      ),
    ).toEqual([
      ["Name", "Address"],
      ["Hilltop, Ltd", "1 High St\nBakewell"],
      ['Say "hi"', "x"],
    ]);
  });

  it("reads semicolon separated files", () => {
    expect(readRows("Name;Phone\nSage;0123")).toEqual([
      ["Name", "Phone"],
      ["Sage", "0123"],
    ]);
  });

  it("matches columns by name or a usual other name, once each", () => {
    const fields = [
      field("commonName", "Common name", ["name", "plant"]),
      field("botanicalName", "Botanical name", ["latin name"]),
      field("variety", "Variety"),
    ];
    expect(matchColumns(["Plant", "Latin Name", "Notes"], fields)).toEqual({
      commonName: 0,
      botanicalName: 1,
      variety: -1,
    });
  });

  it("numbers rows as the spreadsheet does and leaves out empty ones", () => {
    expect(
      mappedRows([["Sage", "x"], ["", ""], ["Thyme"]], {
        commonName: 0,
        variety: -1,
      }),
    ).toEqual([
      { row: 2, values: { commonName: "Sage" } },
      { row: 4, values: { commonName: "Thyme" } },
    ]);
  });

  it("reads Excel's Windows-1252 CSV as well as UTF-8", () => {
    const utf8 = new TextEncoder().encode("Lavandula × intermedia");
    expect(decodeSpreadsheet(utf8.buffer as ArrayBuffer)).toBe(
      "Lavandula × intermedia",
    );
    const windows = new Uint8Array([
      ...new TextEncoder().encode("Lavandula "),
      0xd7,
      ...new TextEncoder().encode(" intermedia"),
    ]);
    expect(decodeSpreadsheet(windows.buffer)).toBe("Lavandula × intermedia");
  });
});
