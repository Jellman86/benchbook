import { expect, test, type Route } from "@playwright/test";

test("phone edits open the customer form and slow climate replies cannot replace the current span", async ({
  page,
}) => {
  await page.setViewportSize({ width: 390, height: 844 });
  let older: Route | null = null;
  let requestedOlder!: () => void;
  const olderRequested = new Promise<void>((resolve) => {
    requestedOlder = resolve;
  });
  const history = (temperature: number) => ({
    locationId: "greenhouse",
    place: "Greenhouse",
    since: "2026-11-12T00:00:00Z",
    hours: [
      {
        hour: "2026-11-13T09:00:00Z",
        temperature,
        lowestTemperature: temperature,
        highestTemperature: temperature,
        humidity: 60,
      },
    ],
  });
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());
    if (url.pathname === "/api/climate/greenhouse") {
      if (url.searchParams.get("days") === "7") {
        older = route;
        requestedOlder();
        return;
      }
      await route.fulfill({ json: history(19) });
      return;
    }
    const data: Record<string, unknown> = {
      "/api/account/me": {
        id: "owner",
        name: "Test owner",
        isOwner: true,
        isTeam: false,
      },
      "/api/bench/people": null,
      "/api/backups/status": { known: true, healthy: true },
      "/api/customers": [
        {
          id: "customer",
          name: "Hilltop",
          address: "High Street",
          email: null,
          phone: null,
          vatNumber: null,
          archived: false,
        },
      ],
      "/api/climate": [
        {
          locationId: "greenhouse",
          place: "Greenhouse",
          latest: null,
          today: null,
        },
      ],
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });
  await page.goto("/");
  const button = (name: string) =>
    page.getByRole("button", { name, exact: true });
  await button("Manage").click();
  await button("Customers").click();
  await button("Change").click();
  await expect(page.getByLabel("Name", { exact: true })).toBeVisible();
  await expect(page.getByLabel("Name", { exact: true })).toBeFocused();
  await expect(page.getByLabel("Name", { exact: true })).toHaveValue("Hilltop");
  await button("Back").click();
  await button("Climate").click();
  await page.locator(".climate-card").click();
  await olderRequested;
  await button("Today").click();
  await page.getByText("Day by day", { exact: true }).click();
  await expect(page.locator(".climate-table")).toContainText("19.0°C");
  await older!.fulfill({ json: history(42) });
  await page.waitForLoadState("networkidle");
  await expect(page.locator(".climate-table")).toContainText("19.0°C");
  await expect(page.locator(".climate-table")).not.toContainText("42.0°C");
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});
