import { describe, expect, it } from "vitest";
import { nestLocations } from "./locations";

const places = [
  { id: "b2", name: "Bench 2", parentId: "g1" },
  { id: "g2", name: "Greenhouse 2", parentId: "farm" },
  { id: "farm", name: "The Farm", parentId: null },
  { id: "b1", name: "Bench 1", parentId: "g1" },
  { id: "g1", name: "Greenhouse 1", parentId: "farm" },
];

describe("nestLocations", () => {
  it("puts each place straight after the one it's inside, in name order", () => {
    expect(nestLocations(places).map((item) => item.place.name)).toEqual([
      "The Farm",
      "Greenhouse 1",
      "Bench 1",
      "Bench 2",
      "Greenhouse 2",
    ]);
  });

  it("records how deep each place is", () => {
    expect(nestLocations(places).map((item) => item.depth)).toEqual([
      0, 1, 2, 2, 1,
    ]);
  });

  it("still lists a place whose parent isn't in the list", () => {
    expect(
      nestLocations([{ id: "x", name: "Lost bench", parentId: "gone" }]),
    ).toEqual([
      { place: { id: "x", name: "Lost bench", parentId: "gone" }, depth: 0 },
    ]);
  });
});
