import { describe, expect, it } from "vitest";
import { findTrays } from "./findTray";
import type { Schemas } from "./types";

const tray = (
  labelCode: string,
  commonName: string,
  variety: string | null,
  locationName: string | null,
) =>
  ({
    id: labelCode,
    labelCode,
    kind: "Tray",
    contents: [
      {
        batchId: `b-${labelCode}`,
        traceabilityCode: `B-${labelCode}`,
        plantId: "p",
        commonName,
        variety,
        quantity: 40,
        source: "SeedLot",
      },
    ],
    locationId: null,
    locationName,
    potSizeId: null,
    potSizeName: null,
    stage: "Sown",
    stageSince: null,
  }) as Schemas["ContainerResponse"];

const trays = [
  tray("C000003", "Salvia", "Hot Lips", "Greenhouse 1, Bench A"),
  tray("C000001", "Lavender", "Hidcote", "Greenhouse 2, Bench B"),
  tray("C000002", "Sea lavender", null, "Greenhouse 1, Bench A"),
];

const codes = (query: string) =>
  findTrays(trays, query).map((container) => container.labelCode);

describe("findTrays", () => {
  it("finds trays by plant, putting names that start with the words first", () => {
    expect(codes("lav")).toEqual(["C000001", "C000002"]);
  });

  it("needs every word, so plant and place narrow it down", () => {
    expect(codes("lavender bench a")).toEqual(["C000002"]);
    expect(codes("hidcote")).toEqual(["C000001"]);
  });

  it("finds a tray by its code or batch code, in any case", () => {
    expect(codes("c000003")).toEqual(["C000003"]);
    expect(codes("b-c000002")).toEqual(["C000002"]);
  });

  it("shows nothing until something is typed", () => {
    expect(codes("  ")).toEqual([]);
  });
});
