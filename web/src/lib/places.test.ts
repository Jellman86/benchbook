import { describe, expect, it } from "vitest";
import {
  findPlaceByCode,
  isPlaceCode,
  possibleParents,
  trayPlaces,
  withEverythingInside,
  type Place,
} from "./places";

const place = (
  id: string,
  name: string,
  parentId: string | null,
  kind: Place["kind"] = "Bench",
): Place => ({
  id,
  name,
  kind,
  parentId,
  archived: false,
  photoIds: [],
  path: name,
  labelCode: `P000${id}`,
  lastUsedAt: null,
});

const farm = place("1", "Farm", null, "Site");
const house = { ...place("2", "Greenhouse 1", "1", "Greenhouse") };
const bench10 = {
  ...place("3", "Bench 10", "2"),
  path: "Greenhouse 1, Bench 10",
};
const bench2 = { ...place("4", "Bench 2", "2"), path: "Greenhouse 1, Bench 2" };
const all = [farm, house, bench10, bench2];

describe("place codes", () => {
  it("tells place labels from tray labels", () => {
    expect(isPlaceCode("P0001")).toBe(true);
    expect(isPlaceCode(" p0012 ")).toBe(true);
    expect(isPlaceCode("C000001")).toBe(false);
  });

  it("finds a place by its code, ignoring capitals", () => {
    expect(findPlaceByCode("p0003", all)?.name).toBe("Bench 10");
  });
});

describe("nesting", () => {
  it("finds everything inside a place", () => {
    expect([...withEverythingInside("1", all)].sort()).toEqual([
      "1",
      "2",
      "3",
      "4",
    ]);
  });

  it("never offers to put a place inside itself", () => {
    expect(possibleParents("2", all).map((item) => item.id)).toEqual(["1"]);
  });

  it("lists places for trays in number order, without the site", () => {
    expect(trayPlaces(all).map((item) => item.label)).toEqual([
      "Greenhouse 1",
      "Greenhouse 1, Bench 2",
      "Greenhouse 1, Bench 10",
    ]);
  });

  it("puts places in use first", () => {
    const used = { ...bench10, lastUsedAt: "2026-11-09T10:00:00Z" };
    expect(
      trayPlaces([farm, house, used, bench2]).map((item) => item.label)[0],
    ).toBe("Greenhouse 1, Bench 10");
  });
});
