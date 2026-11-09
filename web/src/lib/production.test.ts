import { describe, expect, it } from "vitest";
import {
  choicesOf,
  filterRows,
  oldestFirst,
  plantsByStage,
  stageLabel,
  timeAtStage,
  totalPlants,
  type ProductionRow,
} from "./production";

const row = (over: Partial<ProductionRow>): ProductionRow =>
  ({
    containerId: "c",
    labelCode: "C000001",
    batchId: "b",
    traceabilityCode: "B000001",
    plantId: "p",
    commonName: "Lavender",
    variety: "Hidcote",
    botanicalName: "Lavandula angustifolia",
    quantity: 10,
    stage: "Sown",
    stageSince: "2026-11-01T10:00:00Z",
    locationName: "Bench 1",
    potSizeName: "9cm",
    supplierName: "Hillside",
    hasPhoto: false,
    ...over,
  }) as ProductionRow;

const noFilters = { text: "", place: "", supplier: "" };

describe("filterRows", () => {
  const rows = [
    row({ labelCode: "C000001" }),
    row({
      labelCode: "C000002",
      commonName: "Foxglove",
      variety: null,
      botanicalName: "Digitalis purpurea",
      locationName: "Bench 2",
      supplierName: null,
    }),
  ];

  it("keeps everything with no filters", () => {
    expect(filterRows(rows, noFilters)).toHaveLength(2);
  });

  it.each(["hidcote", "LAVANDULA", "c000001"])("finds %s", (text) => {
    expect(
      filterRows(rows, { ...noFilters, text }).map((item) => item.labelCode),
    ).toEqual(["C000001"]);
  });

  it("filters by place and by supplier", () => {
    expect(
      filterRows(rows, { ...noFilters, place: "Bench 2" }).map(
        (item) => item.commonName,
      ),
    ).toEqual(["Foxglove"]);
    expect(
      filterRows(rows, { ...noFilters, supplier: "Hillside" }).map(
        (item) => item.commonName,
      ),
    ).toEqual(["Lavender"]);
  });
});

describe("counts", () => {
  it("adds up plants by stage, not trays", () => {
    const counts = plantsByStage([
      row({ stage: "Sown", quantity: 10 }),
      row({ stage: "Sown", quantity: 5 }),
      row({ stage: "Ready", quantity: 2 }),
    ]);

    expect(counts.get("Sown")).toBe(15);
    expect(counts.get("Ready")).toBe(2);
    expect(counts.get("Germinated")).toBeUndefined();
  });

  it("totals plants, including any with no recorded stage", () => {
    expect(
      totalPlants([row({ quantity: 3 }), row({ quantity: 4, stage: null })]),
    ).toBe(7);
  });
});

describe("oldestFirst", () => {
  it("puts the longest waiting first without changing the original", () => {
    const rows = [
      row({ labelCode: "new", stageSince: "2026-11-05T00:00:00Z" }),
      row({ labelCode: "old", stageSince: "2026-10-01T00:00:00Z" }),
    ];

    expect(oldestFirst(rows).map((item) => item.labelCode)).toEqual([
      "old",
      "new",
    ]);
    expect(rows[0]?.labelCode).toBe("new");
  });
});

describe("timeAtStage", () => {
  const now = new Date("2026-11-09T12:00:00Z");

  it.each([
    ["2026-11-09T09:00:00Z", "today"],
    ["2026-11-08T09:00:00Z", "1 day"],
    ["2026-10-30T09:00:00Z", "10 days"],
    [null, ""],
  ])("for %s says %s", (since, expected) => {
    expect(timeAtStage(since, now)).toBe(expected);
  });
});

describe("labels and choices", () => {
  it("names stages in plain words and admits when there isn't one", () => {
    expect(stageLabel("PottedOn")).toBe("Potted on");
    expect(stageLabel(null)).toBe("Not recorded");
  });

  it("lists distinct values in order, without blanks", () => {
    expect(choicesOf(["b", null, "a", "b", ""])).toEqual(["a", "b"]);
  });
});
