import { describe, expect, it } from "vitest";
import { sortRows, type Column } from "./table";

interface Row {
  name: string;
  count: number | null;
}

const rows: Row[] = [
  { name: "Bench 10", count: 3 },
  { name: "Bench 2", count: null },
  { name: "Bench 1", count: 12 },
];

const name: Column<Row> = { label: "Name", value: (row) => row.name };
const count: Column<Row> = {
  label: "Count",
  value: (row) => row.count,
  numeric: true,
};

describe("sorting", () => {
  it("sorts names the way people count", () => {
    expect(sortRows(rows, name, true).map((row) => row.name)).toEqual([
      "Bench 1",
      "Bench 2",
      "Bench 10",
    ]);
  });

  it("sorts numbers either way, with empty cells last", () => {
    expect(sortRows(rows, count, false).map((row) => row.count)).toEqual([
      12,
      3,
      null,
    ]);
    expect(sortRows(rows, count, true).map((row) => row.count)).toEqual([
      3,
      12,
      null,
    ]);
  });

  it("leaves the order alone with no column chosen", () => {
    expect(sortRows(rows, null, true)).toBe(rows);
  });
});
