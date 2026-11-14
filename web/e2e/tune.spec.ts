import { expect, test } from "./fixtures";

// The owner can give someone a sign-in tune. Phones only play sound a tap started, so the tap on a name both signs
// the person in and lets their tune play.
test("tapping your name to sign in plays your tune", async ({ page }) => {
  const person = {
    id: "0193a6f0-0000-7000-8000-00000000057e",
    name: "Stephen",
    nameOnly: true,
    hasPassword: false,
  };
  let signedIn = false;
  const tuneRequests: string[] = [];

  // A tiny silent WAV.
  const wav = Buffer.from(
    "UklGRiQAAABXQVZFZm10IBAAAAABAAEAQB8AAEAfAAABAAgAZGF0YQAAAAA=",
    "base64",
  );

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());

    if (url.pathname === "/api/account/tune") {
      tuneRequests.push(signedIn ? "after" : "before");
      await route.fulfill({ contentType: "audio/wav", body: wav });
      return;
    }

    if (url.pathname === "/api/account/sign-in/name") {
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
      "/api/account/external": { enabled: false, name: null },
      "/api/account/sign-in-mode": { level: "Names", benchNeedsPin: false },
      "/api/backups/status": { known: true, healthy: true },
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });

  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Who's signing in?" }),
  ).toBeVisible();
  expect(tuneRequests).toEqual([]);

  await page.getByRole("button", { name: "Stephen" }).click();
  await expect.poll(() => tuneRequests).toEqual(["after"]);
});
