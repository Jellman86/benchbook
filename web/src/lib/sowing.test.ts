import { describe, expect, it } from "vitest";
import { lineAmount, sowingWords, startsCounted } from "./sowing";

describe("sowing", () => {
  it("puts each measure in words", () => {
    expect(sowingWords({ sownAs: "About", sownAmount: 200 })).toBe(
      "about 200 seeds",
    );
    expect(sowingWords({ sownAs: "Grams", sownAmount: "0.5" })).toBe("0.5 g");
    expect(
      sowingWords({
        sownAs: "Packet",
        packetShare: "Half",
        packetSays: "approx. 250 seeds",
      }),
    ).toBe("half a packet (approx. 250 seeds)");
    expect(sowingWords({ sownAs: "Counted" })).toBe("");
  });

  it("only a count starts a tray with plants", () => {
    expect(startsCounted("Counted")).toBe(true);
    expect(startsCounted(null)).toBe(true);
    expect(startsCounted("Grams")).toBe(false);
  });

  it("shows an uncounted line in words", () => {
    expect(lineAmount({ quantity: 0, notCounted: true })).toBe(
      "Not counted yet:",
    );
    expect(lineAmount({ quantity: 40 })).toBe("40");
  });
});
