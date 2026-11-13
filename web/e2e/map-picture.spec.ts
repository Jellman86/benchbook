import { expect, test } from "./fixtures";

// Without a satellite key, places are drawn on the nursery's own picture: no address search, and the outline is kept
// as fractions of the picture.
test("a place is drawn on the nursery's own picture", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  // A 400 by 200 picture, drawn in the browser so the test needs no file.
  const picture = await page.evaluate(async () => {
    const canvas = new OffscreenCanvas(400, 200);
    const context = canvas.getContext("2d")!;
    context.fillStyle = "#6b8f4e";
    context.fillRect(0, 0, 400, 200);
    const blob = await canvas.convertToBlob({ type: "image/png" });
    return [...new Uint8Array(await blob.arrayBuffer())];
  });
  let saved: { points: number[][]; onPicture: boolean } | null = null;

  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());

    if (url.pathname === "/api/settings/map/picture") {
      await route.fulfill({
        contentType: "image/png",
        body: Buffer.from(picture),
      });
      return;
    }

    if (url.pathname.endsWith("/outline")) {
      saved = route.request().postDataJSON();
      await route.fulfill({ status: 204 });
      return;
    }

    const site = {
      id: "site",
      name: "Croft",
      kind: "Site",
      parentId: null,
      path: "Croft",
      labelCode: "P0001",
      archived: false,
      photoIds: [],
      lastUsedAt: null,
    };
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Test owner",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/locations": [site],
      "/api/locations/map": {
        imageryKey: null,
        outlines: saved ? [{ id: "site", points: saved.points }] : [],
        onPicture: true,
        pictureChangedAt: "2026-11-13T10:00:00Z",
      },
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });

  await page.goto("/");
  await page
    .getByRole("navigation", { name: "Main" })
    .getByRole("button", { name: "Places" })
    .click();
  await page.locator(".place-row", { hasText: "Croft" }).click();
  await expect(page.getByText("Croft isn't on the map yet.")).toBeVisible();
  await page.getByRole("button", { name: "Draw it on the map" }).click();
  await expect(
    page.getByRole("heading", { name: "Where is Croft?" }),
  ).toBeVisible();
  await expect(page.getByText("On the nursery's own picture.")).toBeVisible();
  await expect(page.getByLabel(/Find a postcode/)).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Finish" })).toBeEnabled();

  const canvas = page.locator(".place-map-canvas");
  const area = (await canvas.boundingBox())!;
  for (const [x, y] of [
    [0.4, 0.4],
    [0.6, 0.4],
    [0.6, 0.6],
    [0.4, 0.6],
  ]) {
    await page.mouse.click(area.x + area.width * x, area.y + area.height * y);
  }
  await page.getByRole("button", { name: "Finish" }).click();
  await page.screenshot({ path: "build/peek/map-picture.png" });
  await page.getByRole("button", { name: "Save the outline" }).click();

  await expect.poll(() => saved).not.toBeNull();
  await expect(page.getByRole("heading", { name: "Croft" })).toBeVisible();
  // Back on the place's page, its outline shows on the picture.
  await expect(page.locator(".place-mini-map-canvas")).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Change it on the map" }),
  ).toBeVisible();
  await page.screenshot({ path: "build/peek/place-page.png" });
  expect(saved!.onPicture).toBe(true);
  for (const [down, across] of saved!.points) {
    expect(down).toBeGreaterThan(0);
    expect(down).toBeLessThan(1);
    expect(across).toBeGreaterThan(0);
    expect(across).toBeLessThan(1);
  }
});
