import { describe, expect, it } from "vitest";
import {
  containerOf,
  describeStep,
  stillGrowing,
  type BatchTrace,
  type TraceStep,
} from "./trace";

const step = (type: TraceStep["type"], extra: Partial<TraceStep> = {}) =>
  ({
    eventId: "e",
    type,
    occurredAt: "2026-11-01T09:00:00Z",
    labelCode: null,
    fromLabelCode: null,
    quantity: null,
    place: null,
    recordedBy: "Sam",
    details: {},
    corrected: false,
    ...extra,
  }) as TraceStep;

describe("describeStep", () => {
  it("says where plants went when they move into a new container", () => {
    expect(
      describeStep(
        step("PrickedOut", {
          quantity: 30,
          labelCode: "C000002",
          fromLabelCode: "C000001",
        }),
      ),
    ).toBe("30 pricked out from C000001 into C000002");
  });

  it("gives losses as a number with the reason", () => {
    expect(
      describeStep(
        step("Lost", { quantity: -4, details: { reason: "Frost" } }),
      ),
    ).toBe("4 lost (Frost)");
    expect(describeStep(step("Sold", { quantity: -20 }))).toBe("20 sold");
  });

  it("gives a treatment's product, dose and area when they were recorded", () => {
    expect(
      describeStep(
        step("Treated", {
          details: { product: "Neem oil", dose: "5ml/L", area: "Whole tray" },
        }),
      ),
    ).toBe("Treated with Neem oil, at 5ml/L, on Whole tray");
    expect(
      describeStep(step("Treated", { details: { product: "Neem oil" } })),
    ).toBe("Treated with Neem oil");
  });

  it("names the place a container moved to", () => {
    expect(
      describeStep(step("Moved", { place: "Greenhouse 1, Bench A" })),
    ).toBe("Moved to Greenhouse 1, Bench A");
  });
});

describe("containerOf", () => {
  it("names the container only where the description doesn't", () => {
    expect(
      containerOf(step("Sown", { quantity: 40, labelCode: "C000001" })),
    ).toBeNull();
    expect(
      containerOf(step("Lost", { quantity: -2, labelCode: "C000001" })),
    ).toBe("C000001");
  });
});

describe("stillGrowing", () => {
  it("leaves out containers the batch has left", () => {
    const trace = {
      containers: [
        { labelCode: "C1", kind: "Tray", now: 0, place: null },
        { labelCode: "C2", kind: "Tray", now: 6, place: "Bench A" },
      ],
    } as unknown as BatchTrace;

    expect(stillGrowing(trace).map((container) => container.labelCode)).toEqual(
      ["C2"],
    );
  });
});
