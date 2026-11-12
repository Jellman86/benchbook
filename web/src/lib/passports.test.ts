import { describe, expect, it } from "vitest";
import { passportProblems } from "./passports";

describe("passportProblems", () => {
  it("explains each refusal", () => {
    expect(passportProblems(409, "NotExamined")).toMatch(/Examine/);
    expect(passportProblems(409, "TooMany")).toMatch(/that many/);
    expect(passportProblems(400)).toMatch(/where the plants are going/);
  });
});
