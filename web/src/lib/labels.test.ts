import { describe, expect, it } from "vitest";
import { sizeLabel, withCopies } from "./labels";

describe("printing labels", () => {
  it("asks for copies only when there's more than one", () => {
    expect(withCopies("/api/labels/plants/p", 1)).toBe("/api/labels/plants/p");
    expect(withCopies("/api/labels/plants/p", 5)).toBe(
      "/api/labels/plants/p?copies=5",
    );
    expect(withCopies("/api/labels/plants/p?potSizeId=z", 2)).toBe(
      "/api/labels/plants/p?potSizeId=z&copies=2",
    );
  });

  it("names sizes width first", () => {
    expect(sizeLabel({ width: 70, height: 25 })).toBe("70 × 25 mm");
  });
});
