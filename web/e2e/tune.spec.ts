import type { Page } from "@playwright/test";
import { expect, test } from "./fixtures";

// The owner can give someone a sign-in tune. Phones only play sound a tap started, so the tap that signs someone in
// first plays a moment of silence on the player, and the tune plays on that same player once signing in has worked.

const person = {
  id: "0193a6f0-0000-7000-8000-00000000057e",
  name: "Stephen",
  nameOnly: true,
  hasPassword: false,
};

// A tiny silent WAV.
const wav = Buffer.from(
  "UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=",
  "base64",
);

/** Records each play() and each sign-in, in the order they happen. */
async function pretendServer(page: Page, signedInAtFirst: boolean) {
  const steps: string[] = [];
  let signedIn = signedInAtFirst;

  await page.exposeBinding("notePlay", (_source, src: string) => {
    steps.push(src.startsWith("data:") ? "silence" : new URL(src).pathname);
  });
  await page.addInitScript(() => {
    const play = HTMLMediaElement.prototype.play;
    HTMLMediaElement.prototype.play = function (this: HTMLMediaElement) {
      void (window as unknown as { notePlay: (src: string) => void }).notePlay(
        this.src,
      );
      return play.call(this);
    };
  });

  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());

    if (url.pathname === "/api/account/tune") {
      await route.fulfill({ contentType: "audio/wav", body: wav });
      return;
    }

    if (url.pathname === "/api/account/sign-in/name") {
      steps.push("sign-in");
      signedIn = true;
      await route.fulfill({ status: 204 });
      return;
    }

    if (url.pathname === "/api/account/me" && !signedIn) {
      await route.fulfill({ status: 401 });
      return;
    }

    if (url.pathname === "/api/account/sign-in/team") {
      await route.fulfill({ status: 403 });
      return;
    }

    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: person.id,
        name: person.name,
        isOwner: false,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/setup/status": { needsSetup: false },
      "/api/account/people": [person],
      "/api/account/external": { enabled: true, name: "Croft View" },
      "/api/account/sign-in-mode": { level: "Names", benchNeedsPin: false },
      "/api/backups/status": { known: true, healthy: true },
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });

  return steps;
}

test("tapping your name to sign in plays your tune", async ({ page }) => {
  const steps = await pretendServer(page, false);

  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Who's signing in?" }),
  ).toBeVisible();
  expect(steps).toEqual([]);

  await page.getByRole("button", { name: "Stephen" }).click();
  await expect
    .poll(() => steps)
    .toEqual(["silence", "sign-in", "/api/account/tune"]);
});

test("after the staff login, the tune plays on the first tap back", async ({
  page,
}) => {
  const steps = await pretendServer(page, true);

  // The Croft View link was tapped before leaving for the staff login.
  await page.addInitScript(() =>
    sessionStorage.setItem("hortiplanner.tunePending", "1"),
  );
  await page.goto("/");
  await expect(page.getByLabel("Scan a label")).toBeVisible();
  expect(steps).toEqual([]);

  await page.locator("main").click({ position: { x: 5, y: 5 } });
  await expect.poll(() => steps).toEqual(["/api/account/tune"]);

  // Once it has played, later taps are just taps.
  await page.locator("main").click({ position: { x: 5, y: 5 } });
  await page.waitForTimeout(300);
  expect(steps).toEqual(["/api/account/tune"]);
});
