import { describe, expect, it } from "vitest";
import {
  consignmentProblem,
  kindChoices,
  overdrawn,
  packagesReady,
  passportsLeft,
} from "./consignments";

describe("consignments", () => {
  it("needs a passport for trade and distance sales only", () => {
    expect(
      kindChoices
        .filter((choice) => choice.needed)
        .map((choice) => choice.kind),
    ).toEqual(["TradeSale", "DistanceSale"]);
  });

  it("finds a tray asked for more than it holds across packages", () => {
    const holds = (tray: string) => (tray === "a" ? 10 : 5);
    expect(
      overdrawn(
        [
          [{ tray: "a", quantity: 6 }],
          [
            { tray: "a", quantity: 5 },
            { tray: "b", quantity: 5 },
          ],
        ],
        holds,
      ),
    ).toEqual(["a"]);
    expect(overdrawn([[{ tray: "a", quantity: 10 }]], holds)).toEqual([]);
  });

  it("is ready only when every package has trays with plants", () => {
    expect(packagesReady([[{ tray: "a", quantity: 1 }]])).toBe(true);
    expect(packagesReady([[{ tray: "a", quantity: 1 }], []])).toBe(false);
    expect(packagesReady([[{ tray: "", quantity: 1 }]])).toBe(false);
    expect(packagesReady([[{ tray: "a", quantity: 0 }]])).toBe(false);
  });

  it("says why it wasn't recorded", () => {
    expect(consignmentProblem(409, "TooMany")).toMatch(/doesn't hold/);
    expect(consignmentProblem(400)).toMatch(/Say who/);
  });

  it("counts the passports still to issue", () => {
    expect(
      passportsLeft({
        passportNeeded: true,
        packages: 3,
        packagesPassported: 1,
      }),
    ).toBe(2);
    expect(
      passportsLeft({
        passportNeeded: false,
        packages: 3,
        packagesPassported: 0,
      }),
    ).toBe(0);
  });
});
