import { describe, expect, it } from "vitest";
import { batchChanges, type BatchForm } from "./batchEdit";

const plants = [
  { id: "p1", commonName: "Lavender", variety: "Hidcote" },
  { id: "p2", commonName: "Salvia", variety: null },
];
const suppliers = [{ id: "s1", name: "Hillside Plants" }];

const before: BatchForm = {
  plantId: "p1",
  source: "SeedLot",
  sourceReference: "Lot 7",
  supplierId: "s1",
  motherPlant: "",
  originCountry: "GB",
  startedOn: "2026-11-03",
  quantity: 40,
  sowBy: "",
};

describe("batchChanges", () => {
  it("lists a lot reference that saving clears, even when only the country changed", () => {
    const cuttings = { ...before, source: "Cuttings" as const, supplierId: "" };

    expect(
      batchChanges(
        cuttings,
        { ...cuttings, originCountry: "NL" },
        plants,
        suppliers,
      ).map((change) => change.name),
    ).toEqual(["Lot or reference", "Country of origin"]);
  });

  it("lists nothing when nothing changed, ignoring spaces and case", () => {
    expect(
      batchChanges(
        before,
        { ...before, sourceReference: " Lot 7 ", originCountry: "gb" },
        plants,
        suppliers,
      ),
    ).toEqual([]);
  });

  it("names what changed, before and after, in words", () => {
    const changes = batchChanges(
      before,
      { ...before, plantId: "p2", originCountry: "NL", quantity: 36 },
      plants,
      suppliers,
    );

    expect(changes).toEqual([
      { name: "Plant", before: "Lavender 'Hidcote'", after: "Salvia" },
      { name: "How many", before: "40", after: "36" },
      {
        name: "Country of origin",
        before: "Great Britain",
        after: "Netherlands",
      },
    ]);
  });

  it("drops the supplier and lot when plants become cuttings", () => {
    const changes = batchChanges(
      before,
      { ...before, source: "Cuttings", motherPlant: "Big one by the door" },
      plants,
      suppliers,
    );

    expect(changes.map((change) => change.name)).toEqual([
      "Source",
      "Supplier",
      "Lot or reference",
      "Mother plant",
    ]);
  });
});
