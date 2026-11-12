import { describe, expect, it } from "vitest";
import { issueHint, passportProblems } from "./passports";

describe("passportProblems", () => {
  it("explains each refusal", () => {
    expect(passportProblems(409, "NotExamined")).toMatch(/Examine/);
    expect(passportProblems(409, "TooMany")).toMatch(/that many/);
    expect(passportProblems(409, "Changed")).toMatch(/Part D has changed/);
    expect(passportProblems(400)).toMatch(/where the plants are going/);
  });
});

describe("issueHint", () => {
  const ready = {
    examined: true,
    issuedTo: "Hilltop",
    quantity: 10,
    available: 40,
    blocked: false,
  };

  it("is empty once everything is done", () => {
    expect(issueHint(ready)).toBe("");
  });

  it("says what's still needed", () => {
    expect(issueHint({ ...ready, examined: false, issuedTo: " " })).toBe(
      "To issue it, say where the plants are going and tick that you've examined them.",
    );
    expect(issueHint({ ...ready, quantity: 41 })).toMatch(/between 1 and 40/);
  });
});
