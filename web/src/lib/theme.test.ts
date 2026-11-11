import { describe, expect, it } from "vitest";
import { nextTheme } from "./theme";

describe("themes", () => {
  it("goes from light, the default, to dark, then to the device's own", () => {
    expect(nextTheme("light")).toBe("dark");
    expect(nextTheme("dark")).toBe("system");
    expect(nextTheme("system")).toBe("light");
  });
});
