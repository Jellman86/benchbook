import { describe, expect, it } from "vitest";
import type { Schemas } from "./types";
import {
  canGerminate,
  describeHistory,
  likelyNext,
  nextPotSize,
  otherActions,
  quantityPresets,
  whenWords,
} from "./tray";

const line = (source: Schemas["BatchSource"]) =>
  ({
    batchId: "b",
    traceabilityCode: "T1",
    plantId: "p",
    commonName: "Lavender",
    variety: null,
    quantity: 40,
    source,
  }) as Schemas["ContainerLine"];

const tray = (
  stage: Schemas["ProductionStage"] | null,
  source: Schemas["BatchSource"] = "SeedLot",
) =>
  ({
    id: "c",
    labelCode: "C-1",
    kind: "Tray",
    contents: [line(source)],
    locationId: null,
    locationName: null,
    potSizeId: null,
    potSizeName: null,
    stage,
    stageSince: null,
  }) as Schemas["ContainerResponse"];

describe("trays", () => {
  it("offers germination only for seed that hasn't germinated", () => {
    expect(canGerminate(tray("Sown"))).toBe(true);
    expect(canGerminate(tray("Cuttings", "Cuttings"))).toBe(false);
    expect(canGerminate(tray("Germinated"))).toBe(false);
  });

  it("puts the likely next step first", () => {
    expect(likelyNext(tray("Sown"))).toEqual(["germinated", "lost"]);
    expect(likelyNext(tray("Germinated"))).toEqual(["prickOut", "lost"]);
    expect(likelyNext(tray("PottedOn"))).toEqual(["ready", "potOn"]);
  });

  it("never offers germinated on cuttings", () => {
    const cuttings = tray("Cuttings", "Cuttings");
    expect([...likelyNext(cuttings), ...otherActions(cuttings)]).not.toContain(
      "germinated",
    );
  });

  it("doesn't repeat the likely steps among the rest", () => {
    const sown = tray("Sown");
    expect(otherActions(sown)).not.toContain("germinated");
    expect(otherActions(sown)).toContain("move");
  });
});

describe("history", () => {
  const entry = (
    type: Schemas["EventType"],
    quantity: number | null,
    details: unknown = {},
  ) =>
    ({
      eventId: "e",
      type,
      occurredAt: "2026-11-08T10:00:00Z",
      recordedBy: "Jo Reed",
      quantity,
      locationName: "Greenhouse 2, Bench A",
      potSizeName: "9cm",
      details,
      undone: false,
    }) as Schemas["HistoryEntry"];

  it("describes losses with their reason", () => {
    expect(describeHistory(entry("Lost", -3, { reason: "Pest" }))).toMatch(
      /^Lost 3/,
    );
  });

  it("describes a move with the full place", () => {
    expect(describeHistory(entry("Moved", null))).toBe(
      "Moved to Greenhouse 2, Bench A",
    );
  });

  it("describes plants arriving from potting on", () => {
    expect(describeHistory(entry("PottedOn", 12))).toBe(
      "Potted on here: 12 into 9cm",
    );
    expect(describeHistory(entry("PottedOn", -12))).toBe("Potted on: 12");
  });
});

describe("when", () => {
  const now = new Date(2026, 10, 10, 15, 0);

  it("says today and yesterday", () => {
    expect(whenWords(new Date(2026, 10, 10, 9, 0).toISOString(), now)).toBe(
      "today",
    );
    expect(whenWords(new Date(2026, 10, 9, 22, 0).toISOString(), now)).toBe(
      "yesterday",
    );
  });

  it("counts days in the last week, then gives the date", () => {
    expect(whenWords(new Date(2026, 10, 7).toISOString(), now)).toBe(
      "3 days ago",
    );
    expect(whenWords(new Date(2026, 10, 1).toISOString(), now)).toBe(
      "Sun 1 Nov",
    );
  });
});

describe("pot sizes", () => {
  const sizes = [
    { id: "a", name: "2L" },
    { id: "b", name: "9cm" },
    { id: "c", name: "1L" },
    { id: "d", name: "Seed tray" },
  ];

  it("suggests the next size up", () => {
    expect(nextPotSize(sizes, "b")).toBe("c");
    expect(nextPotSize(sizes, "c")).toBe("a");
  });

  it("falls back to another size when the current one can't be compared", () => {
    expect(nextPotSize(sizes, "d")).toBe("a");
    expect(nextPotSize(sizes, null)).toBe("a");
  });

  it("offers half and all", () => {
    expect(quantityPresets(40)).toEqual([20, 40]);
    expect(quantityPresets(1)).toEqual([1]);
  });
});
