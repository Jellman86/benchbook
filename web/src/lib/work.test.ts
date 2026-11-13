import { describe, expect, it } from "vitest";
import type { Schemas } from "./types";
import {
  describeRecent,
  money,
  outcomeProblem,
  percent,
  placeLabel,
  plantName,
  repeatOf,
  retainIdentity,
  work,
  workPlaces,
  shortCredit,
} from "./work";

const line: Schemas["RecentLine"] = {
  containerId: "c1",
  batchId: "b1",
  quantityChange: 40,
  plantId: "p1",
  commonName: "Lavender",
  variety: "Hidcote",
  source: "SeedLot",
  sourceReference: "LOT-1",
  supplierId: "s1",
  originCountry: "GB",
};

const action = (
  overrides: Partial<Schemas["RecentAction"]>,
): Schemas["RecentAction"] => ({
  id: "e1",
  type: "Sown",
  occurredAt: "2026-11-08T10:00:00Z",
  containerId: "c1",
  labelCode: "C000001",
  containerKind: "Tray",
  locationId: "l1",
  locationName: "Bench 1",
  potSizeId: "z1",
  potSizeName: "9cm",
  details: {},
  lines: [line],
  recordedBy: "Jo Reed",
  ...overrides,
});

const loss = action({
  type: "Lost",
  details: { reason: "DampingOff" },
  lines: [{ ...line, quantityChange: -3 }],
});

describe("percent", () => {
  it("shows a whole percentage", () => {
    expect(percent(0.925)).toBe("93%");
    expect(percent(1)).toBe("100%");
    expect(percent(0)).toBe("0%");
  });

  it("shows a dash when there's nothing to go on", () => {
    expect(percent(null)).toBe("–");
  });
});

describe("money", () => {
  it("shows pounds and pence", () => {
    expect(money(4.5)).toBe("£4.50");
    expect(money(0)).toBe("£0.00");
  });
});

describe("plantName", () => {
  it("adds a variety in quotes only when there is one", () => {
    expect(plantName("Lavender", "Hidcote")).toBe("Lavender 'Hidcote'");
    expect(plantName("Rosemary", null)).toBe("Rosemary");
  });
});

describe("describeRecent", () => {
  it("describes a sowing with where it went", () => {
    expect(describeRecent(action({}))).toBe(
      "Sowed 40 Lavender 'Hidcote', Bench 1",
    );
  });

  it("describes a loss in plain words, as a positive number", () => {
    expect(describeRecent(loss)).toBe(
      "Lost 3 Lavender 'Hidcote' (Damping off)",
    );
  });

  it("names the label for actions on a container", () => {
    expect(describeRecent(action({ type: "Germinated", lines: [] }))).toBe(
      "C000001 germinated",
    );
    expect(describeRecent(action({ type: "Ready", lines: [] }))).toBe(
      "C000001 marked ready",
    );
    expect(
      describeRecent(
        action({ type: "Treated", lines: [], details: { product: "Neem" } }),
      ),
    ).toBe("Treated C000001 with Neem");
  });
});

describe("repeatOf", () => {
  it("starts a new batch of the same thing, as new work", () => {
    const repeat = repeatOf(action({}));

    expect(repeat?.url).toBe("/api/batches");
    expect(repeat?.body).toMatchObject({
      plantId: "p1",
      quantity: 40,
      source: "SeedLot",
      sourceReference: "LOT-1",
      supplierId: "s1",
      originCountry: "GB",
      containerKind: "Tray",
      locationId: "l1",
      potSizeId: "z1",
    });
    expect(repeat?.body.eventId).not.toBe("e1");
  });

  it("repeats a loss on the same container with a positive quantity", () => {
    const repeat = repeatOf(loss);

    expect(repeat?.url).toBe("/api/containers/c1/losses");
    expect(repeat?.body).toMatchObject({
      batchId: "b1",
      quantity: 3,
      reason: "DampingOff",
    });
  });

  it("repeats a treatment with the same details", () => {
    const repeat = repeatOf(
      action({
        type: "Treated",
        lines: [],
        details: { product: "Neem", dose: "5ml", area: "Tray" },
      }),
    );

    expect(repeat?.url).toBe("/api/containers/c1/treatments");
    expect(repeat?.body).toMatchObject({
      product: "Neem",
      dose: "5ml",
      area: "Tray",
    });
  });

  it("keeps the mother when repeating cuttings", () => {
    const repeat = repeatOf(
      action({
        type: "CuttingsTaken",
        lines: [
          {
            ...line,
            source: "Cuttings",
            motherPlant: "Hidcote mother",
            motherPlantId: "m1",
          },
        ],
      }),
    );
    expect(repeat?.body).toMatchObject({
      motherPlant: "Hidcote mother",
      motherPlantId: "m1",
    });
  });

  it("gives every repeat its own identifier", () => {
    expect(repeatOf(action({}))?.body.eventId).not.toBe(
      repeatOf(action({}))?.body.eventId,
    );
  });

  it("doesn't offer repeats that would make no sense", () => {
    expect(repeatOf(action({ type: "Moved", lines: [] }))).toBeNull();
    expect(repeatOf(action({ type: "StockCounted" }))).toBeNull();
  });
});

describe("work.undo", () => {
  it("is a correction with no replacement", () => {
    const undo = work.undo("e1");

    expect(undo.url).toBe("/api/events/e1/correct");
    expect(undo.body).toMatchObject({ quantity: null, locationId: null });
  });
});

describe("outcomeProblem", () => {
  it("explains a count that would go below zero", () => {
    expect(outcomeProblem({ kind: "refused", problem: "BelowZero" })).toContain(
      "aren't that many",
    );
  });

  it("explains a lost connection", () => {
    expect(outcomeProblem({ kind: "unreachable" })).toContain("Can't reach");
  });
});

const place = (
  id: string,
  name: string,
  kind: Schemas["LocationKind"],
  parentId: string | null,
): Schemas["LocationResponse"] => ({
  id,
  name,
  kind,
  parentId,
  archived: false,
  photoIds: [],
  // Left empty so the label is worked out here from the places it sits in.
  path: "",
  labelCode: `P${id}`,
  lastUsedAt: null,
});

describe("places", () => {
  const all = [
    place("s", "Croft", "Site", null),
    place("g", "Greenhouse 2", "Greenhouse", "s"),
    place("b", "Bench 1", "Bench", "g"),
  ];

  it("shows a bench with its greenhouse, but a greenhouse without its site", () => {
    expect(placeLabel(all[2], all)).toBe("Greenhouse 2, Bench 1");
    expect(placeLabel(all[1], all)).toBe("Greenhouse 2");
  });

  it("leaves sites out of the places a container can stand in", () => {
    expect(workPlaces(all).map((item) => item.id)).toEqual(["g", "b"]);
  });
});

describe("photo credits", () => {
  it("names the photographer once", () => {
    expect(
      shortCredit(
        "(c) Jo Reed, some rights reserved (CC BY), uploaded by Jo Reed",
      ),
    ).toBe("(c) Jo Reed, some rights reserved (CC BY)");
  });

  it("leaves other credits alone", () => {
    expect(shortCredit("(c) Jo Reed (CC BY)")).toBe("(c) Jo Reed (CC BY)");
  });
});

describe("retry identities", () => {
  it("keeps the first identifier and time across a lost reply", () => {
    const first = work.note(
      "tray",
      { note: "Checked" },
      "2026-11-13T10:00:00Z",
    );
    const later = {
      ...first,
      body: {
        ...first.body,
        eventId: "different",
        occurredAt: "2026-11-13T10:01:00Z",
      },
    };
    expect(retainIdentity(later, first)).toEqual(first);
    expect(retainIdentity(later, null)).toEqual(later);
  });
});
