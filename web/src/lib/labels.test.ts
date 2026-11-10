import { describe, expect, it } from "vitest";
import {
  kindsOfLabel,
  sizeAdvice,
  sizeLabel,
  widestPrint,
  withCopies,
} from "./labels";

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

  it("only offers roll sizes the printer can print", () => {
    for (const kind of kindsOfLabel) {
      expect(kind.sizes.length).toBeGreaterThan(0);

      for (const size of kind.sizes) {
        expect(size.width).toBeLessThanOrEqual(widestPrint);
        expect(sizeAdvice(size)).toEqual({ problem: "", warning: "" });
      }
    }
  });

  it("refuses a size outside what can be saved and warns about one too wide to print", () => {
    expect(sizeAdvice({ width: 10, height: 25 }).problem).toContain(
      "between 20 and 300 mm",
    );
    expect(sizeAdvice({ width: 50, height: Number.NaN }).problem).not.toBe("");
    expect(sizeAdvice({ width: 105, height: 148 }).warning).toContain("104 mm");
    expect(sizeAdvice({ width: 105, height: 148 }).problem).toBe("");
  });
});
