import { expect, test } from "@playwright/test";

// On a desk, Production is a table: choosing several picks trays by a click on the row or its tick box.
test("production's table chooses trays on a desk", async ({ page }) => {
  await page.setViewportSize({ width: 1280, height: 800 });
  const row = (labelCode: string, commonName: string) => ({
    containerId: `tray-${labelCode}`,
    labelCode,
    batchId: `batch-${labelCode}`,
    traceabilityCode: `B-${labelCode}`,
    plantId: "plant",
    commonName,
    variety: null,
    botanicalName: null,
    quantity: 20,
    stage: "Sown",
    stageSince: "2026-11-01T09:00:00Z",
    locationName: "Greenhouse 1",
    potSizeName: null,
    supplierName: "Chiltern seeds",
    hasPhoto: false,
    locationId: "greenhouse",
    notCounted: false,
  });
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Test owner",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/production": [
        row("C000002", "Forking Larkspur"),
        row("C000003", "Cornflower"),
      ],
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });

  await page.goto("/");
  await page
    .getByRole("navigation", { name: "Main" })
    .getByRole("button", { name: "Production" })
    .click();
  await page.getByRole("button", { name: "Choose several" }).click();

  const table = page.locator(".data-table");
  await table.getByRole("row", { name: /C000002/ }).click();
  await expect(page.getByText("1 tray chosen")).toBeVisible();
  await expect(
    table.getByRole("checkbox", { name: "Choose C000002" }),
  ).toBeChecked();

  await table.getByRole("checkbox", { name: "Choose C000003" }).click();
  await expect(page.getByText("2 trays chosen")).toBeVisible();

  await table.getByRole("row", { name: /C000002/ }).click();
  await expect(page.getByText("1 tray chosen")).toBeVisible();
  await expect(
    table.getByRole("checkbox", { name: "Choose C000002" }),
  ).not.toBeChecked();
});
