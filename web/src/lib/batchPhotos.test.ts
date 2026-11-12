import { describe, expect, it } from "vitest";
import { photoCaption } from "./batchPhotos";

describe("photo captions", () => {
  it("gives the day and stage of a photo of the plants", () => {
    expect(
      photoCaption({ kind: "Growing", day: 12, stage: "Germinated" }),
    ).toBe("Day 12 · Germinated");
  });

  it("names what else a photo is of", () => {
    expect(photoCaption({ kind: "Packet", day: 1, stage: "Sown" })).toBe(
      "Day 1 · Seed packet",
    );
    expect(photoCaption({ kind: "Growing", day: null, stage: null })).toBe(
      "Photo",
    );
  });
});
