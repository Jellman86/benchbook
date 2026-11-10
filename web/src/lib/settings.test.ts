import { describe, expect, it } from "vitest";
import { canOpen, movedToSettings, settingsGroups } from "./settings";

describe("settings", () => {
  it("gives staff their own account and About only", () => {
    const pages = settingsGroups(false).flatMap((group) =>
      group.items.map((item) => item.page),
    );

    expect(pages).toEqual(["account", "about"]);
    expect(canOpen("staff", false)).toBe(false);
  });

  it("gives the owner everything, each page once", () => {
    const pages = settingsGroups(true).flatMap((group) =>
      group.items.map((item) => item.page),
    );

    expect(new Set(pages).size).toBe(pages.length);
    expect(pages).toContain("signingIn");
    expect(canOpen("data", true)).toBe(true);
  });

  it("finds the screens that used to be under Manage", () => {
    expect(movedToSettings("signIn")).toBe("signingIn");
    expect(movedToSettings("staff")).toBe("staff");
    expect(movedToSettings("plants")).toBeNull();
    expect(movedToSettings(undefined)).toBeNull();
  });
});
