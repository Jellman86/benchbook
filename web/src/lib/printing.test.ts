import { describe, expect, it } from "vitest";
import { labelOf } from "./printing";

describe("printing", () => {
  it("knows which label a link prints", () => {
    expect(labelOf("/api/labels/containers/C000001")).toEqual({
      kind: "ContainerLabel",
      target: "C000001",
      potSizeId: null,
    });
    expect(labelOf("/api/labels/plants/abc?potSizeId=def")).toEqual({
      kind: "PlantLabel",
      target: "abc",
      potSizeId: "def",
    });
    expect(labelOf("/api/labels/bench-cards/abc")?.kind).toBe("BenchCard");
    expect(labelOf("/api/labels/places/abc")?.kind).toBe("PlaceLabel");
    expect(labelOf("/api/invoices/abc/pdf")).toBeNull();
  });
});
