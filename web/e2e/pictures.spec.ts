import { expect, test } from "./fixtures";

// Someone chooses their own picture in My account. It shows beside their name straight away, stays after leaving
// the page and coming back, and can be removed again.
test("your picture stays after leaving My account", async ({ page }) => {
  let pictureAt: string | null = null;
  const png = Buffer.from(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==",
    "base64",
  );

  await page.setViewportSize({ width: 1280, height: 900 });
  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());

    if (url.pathname === "/api/account/picture") {
      pictureAt = request.method() === "PUT" ? "2026-11-14T11:00:00Z" : null;
      await route.fulfill({ status: 204 });
      return;
    }

    if (url.pathname.endsWith("/picture")) {
      await route.fulfill({ contentType: "image/png", body: png });
      return;
    }

    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "0193a6f0-0000-7000-8000-0000000000aa",
        name: "Nicola Powdrill",
        isOwner: false,
        isTeam: false,
        pictureAt,
      },
      "/api/account/settings": {
        hasPassword: true,
        hasPin: false,
        hasPasskey: false,
        hasExternalLogin: false,
        externalSignInName: null,
        nameOnly: false,
        hasTune: false,
        tuneOn: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });

  await page.goto("/");
  await page.getByRole("button", { name: "Settings" }).click();
  await page
    .getByRole("button", { name: /My account/ })
    .first()
    .click();
  await page
    .locator(".photo-choice input[type=file]")
    .first()
    .setInputFiles({ name: "me.png", mimeType: "image/png", buffer: png });
  await expect(page.getByRole("status")).toContainText(
    "Your picture has been saved.",
  );
  await expect(
    page.getByRole("button", { name: "Remove your picture" }),
  ).toBeVisible();
  await expect(page.locator(".sidebar-foot img.avatar")).toBeVisible();

  // Away and back again: still the new picture, not the old state.
  await page.getByRole("button", { name: "Record work" }).click();
  await page.getByRole("button", { name: "Settings" }).click();
  await page
    .getByRole("button", { name: /My account/ })
    .first()
    .click();
  await expect(
    page.getByRole("button", { name: "Remove your picture" }),
  ).toBeVisible();

  await page.getByRole("button", { name: "Remove your picture" }).click();
  await expect(page.getByRole("status")).toContainText(
    "Your picture has been removed.",
  );
  await expect(page.locator(".sidebar-foot img.avatar")).toHaveCount(0);
});
