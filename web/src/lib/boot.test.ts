import { describe, expect, it, vi } from "vitest";
import { chooseStart, readInvite, type Probes } from "./boot";

const probes = (overrides: Partial<Probes> = {}): Probes => ({
  me: async () => null,
  benchPeople: async () => null,
  needsSetup: async () => false,
  ...overrides,
});

const me = { name: "Sam", isOwner: true };
const people = [{ id: "1", name: "Alex" }];

describe("chooseStart", () => {
  it("goes home when someone is signed in, even on a bench station", async () => {
    const start = await chooseStart(
      probes({ me: async () => me, benchPeople: async () => people }),
      null,
    );

    expect(start).toEqual({ screen: "home", me, atBench: true });
  });

  it("knows a signed-in person on their own device isn't at a bench", async () => {
    expect(await chooseStart(probes({ me: async () => me }), null)).toEqual({
      screen: "home",
      me,
      atBench: false,
    });
  });

  it("shows the name list on a bench station with nobody signed in", async () => {
    expect(
      await chooseStart(probes({ benchPeople: async () => people }), null),
    ).toEqual({ screen: "bench", people });
  });

  it("shows setup on a new system", async () => {
    expect(
      await chooseStart(probes({ needsSetup: async () => true }), null),
    ).toEqual({ screen: "setup" });
  });

  it("shows sign-in otherwise", async () => {
    expect(await chooseStart(probes(), null)).toEqual({ screen: "signIn" });
  });

  it("puts an invitation first, without asking the server anything", async () => {
    const invite = { userId: "1", token: "t" };
    const meProbe = vi.fn(async () => me);

    expect(await chooseStart(probes({ me: meProbe }), invite)).toEqual({
      screen: "invite",
      invite,
    });
    expect(meProbe).not.toHaveBeenCalled();
  });
});

describe("readInvite", () => {
  it("reads the person and token from an invitation link", () => {
    expect(readInvite("/invite", "?user=abc&token=x%2By")).toEqual({
      userId: "abc",
      token: "x+y",
    });
  });

  it("ignores other pages and incomplete links", () => {
    expect(readInvite("/", "?user=abc&token=x")).toBeNull();
    expect(readInvite("/invite", "?user=abc")).toBeNull();
  });
});
