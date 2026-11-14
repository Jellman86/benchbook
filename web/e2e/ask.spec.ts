import { type Page } from "@playwright/test";
import { expect, test } from "./fixtures";

// Ask Hortiplanner against a pretend server: offered on Record and in the sidebar, answers shown as plain text with the
// records they looked at, and a desk using its width for them.
async function pretend(page: Page, asked: unknown[]) {
  await page.route("**/api/**", async (route) => {
    const url = new URL(route.request().url());

    if (url.pathname === "/api/assistant/ask") {
      asked.push(route.request().postDataJSON());
      await route.fulfill({
        json: {
          answer:
            "## Worth a look\n\n- **C000012** has been sown 21 days without germinating.\n- <img src=x onerror=alert(1)>\n\nCheck the compost is moist and the tray is warm enough.",
          sources: [
            { kind: "tray", key: "C000012", label: "C000012 Lavender" },
          ],
          cost: 0.0021,
          spentThisMonth: 0.4221,
          spentInAll: 1.5021,
        },
      });
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
      "/api/assistant/status": {
        canAsk: true,
        setUp: true,
        readsImages: true,
        spent: { thisMonth: 0.42, inAll: 1.5 },
      },
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });
}

test("Ask Hortiplanner answers on a phone, and lays out on a desk", async ({
  page,
}) => {
  const asked: unknown[] = [];
  await pretend(page, asked);
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto("/");

  await page.getByRole("button", { name: /Ask Hortiplanner/ }).click();
  await expect(
    page.getByRole("heading", { name: "Ask Hortiplanner" }),
  ).toBeVisible();
  // The nursery pays for every question, so each person sees what theirs have cost.
  await expect(page.locator(".ask-spend")).toContainText("$0.42");
  const crumbs = page.getByRole("navigation", { name: "Breadcrumb" });
  await expect(crumbs.locator("[aria-current=page]")).toHaveText(
    "Ask Hortiplanner",
  );

  await page
    .getByRole("button", { name: "What needs attention this week?" })
    .click();
  const answer = page.locator(".ask-answer").first();
  await expect(answer).toContainText("C000012 has been sown 21 days");
  // The model's words are shown as text, never as markup.
  await expect(answer).toContainText("<img src=x onerror=alert(1)>");
  await expect(answer.locator("img")).toHaveCount(0);
  await expect(
    page.getByRole("button", { name: "C000012 Lavender" }),
  ).toBeVisible();
  expect(asked).toHaveLength(1);
  expect((asked[0] as { messages: { text: string }[] }).messages[0].text).toBe(
    "What needs attention this week?",
  );
  await page.screenshot({ path: "build/peek/ask-phone.png", fullPage: true });

  await page.setViewportSize({ width: 1280, height: 800 });
  await expect(page.getByRole("heading", { name: "Looked at" })).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "What your questions cost" }),
  ).toBeVisible();
  await expect(page.locator(".ask-spend-card")).toContainText("$1.50");
  await page.screenshot({ path: "build/peek/ask-desk.png" });
  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});
