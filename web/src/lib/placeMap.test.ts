import { describe, expect, it } from "vitest";
import { alwaysNamed, corners, imagery, postcodeIn } from "./placeMap";

describe("the places map", () => {
  it("uses satellite pictures only with a key", () => {
    expect(imagery("AAPK x").url).toContain("token=AAPK%20x");
    expect(imagery("AAPK x").attribution).toContain("Powered by");
    expect(imagery(null).satellite).toBe(false);
    expect(imagery(null).attribution).toContain("OpenStreetMap");
  });

  it("finds a postcode in an address", () => {
    expect(postcodeIn("Croft Lane\nMatlock\nde4 2ab")).toBe("DE4 2AB");
    expect(postcodeIn("SW1A1AA")).toBe("SW1A 1AA");
    expect(postcodeIn("Croft Lane")).toBeNull();
    expect(postcodeIn(null)).toBeNull();
  });

  it("keeps each corner once", () => {
    expect(
      corners([
        { lat: 1, lng: 2 },
        { lat: 3, lng: 4 },
        { lat: 5, lng: 6 },
        { lat: 1, lng: 2 },
      ]),
    ).toEqual([
      [1, 2],
      [3, 4],
      [5, 6],
    ]);
  });

  it("names big places on the map and leaves benches to a touch", () => {
    expect(alwaysNamed("Greenhouse")).toBe(true);
    expect(alwaysNamed("Bench")).toBe(false);
  });
});
