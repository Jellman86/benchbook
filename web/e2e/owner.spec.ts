import { expect, test, type Page } from "@playwright/test";

/** Gives this browser context a built-in passkey that approves requests by itself. */
async function addPasskeySupport(page: Page) {
  const session = await page.context().newCDPSession(page);
  await session.send("WebAuthn.enable");
  await session.send("WebAuthn.addVirtualAuthenticator", {
    options: {
      protocol: "ctap2",
      transport: "internal",
      hasResidentKey: true,
      hasUserVerification: true,
      isUserVerified: true,
      automaticPresenceSimulation: true,
    },
  });
}

const button = (page: Page, name: string) =>
  page.getByRole("button", { name, exact: true });

async function openManage(page: Page, section: string) {
  await button(page, "Manage").click();
  await button(page, section).click();
  await expect(page.getByRole("heading", { name: section })).toBeVisible();
}

async function leaveManage(page: Page) {
  await button(page, "Back").click();
  await button(page, "Back").click();
}

// One story, in order: each step relies on what the last one set up.
test("the owner sets up the nursery and staff can sign in", async ({
  page,
  browser,
}) => {
  await addPasskeySupport(page);
  page.on("dialog", (dialog) => void dialog.accept());

  // Owner setup, refused with the wrong code first.
  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Set up GrowLog" }),
  ).toBeVisible();
  await page.getByLabel("Setup code").fill("wrong");
  await page.getByLabel("Your name").fill("Sam Fielding");
  await button(page, "Create my passkey").click();
  await expect(page.getByRole("alert")).toHaveText(
    "That setup code isn't right.",
  );

  await page.getByLabel("Setup code").fill("e2e-setup-code");
  await button(page, "Create my passkey").click();
  await expect(
    page.getByRole("heading", { name: "Hello, Sam Fielding" }),
  ).toBeVisible();

  // Signing out and back in uses the passkey just made.
  await button(page, "Finished").click();
  await button(page, "Sign in with passkey").click();
  await expect(
    page.getByRole("heading", { name: "Hello, Sam Fielding" }),
  ).toBeVisible();

  // A reload keeps the session.
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Hello, Sam Fielding" }),
  ).toBeVisible();

  // Catalogue.
  await openManage(page, "Pot sizes");
  await page.getByLabel("New pot size").fill("9cm");
  await button(page, "Add").click();
  await expect(page.getByText("9cm", { exact: true })).toBeVisible();
  await page.getByLabel("New pot size").fill("9CM");
  await button(page, "Add").click();
  await expect(page.getByRole("alert")).toHaveText(
    "That pot size is already on the list.",
  );
  await leaveManage(page);

  await openManage(page, "Plants");
  await page.getByLabel("Common name").fill("Lavender");
  await page.getByLabel("Variety", { exact: true }).fill("Hidcote");
  await page.getByLabel("Botanical name").fill("Lavandula angustifolia");
  await page.getByLabel("Price for 9cm (£)").fill("4.50");
  await button(page, "Add").click();
  await expect(page.getByText("Lavender 'Hidcote'")).toBeVisible();

  await button(page, "Change").click();
  await expect(page.getByLabel("Price for 9cm (£)")).toHaveValue("4.5");
  await page.getByLabel("Variety", { exact: true }).fill("Munstead");
  await button(page, "Save changes").click();
  await expect(page.getByText("Lavender 'Munstead'")).toBeVisible();
  await leaveManage(page);

  await openManage(page, "Suppliers");
  await page.getByLabel("Name", { exact: true }).fill("Hillside Plants");
  await button(page, "Add").click();
  await expect(page.getByText("Hillside Plants")).toBeVisible();
  await leaveManage(page);

  await openManage(page, "Places");
  await page.getByLabel("Name", { exact: true }).fill("Croft");
  await button(page, "Add").click();
  await expect(
    page.locator(".card strong", { hasText: "Croft" }),
  ).toBeVisible();
  await page.getByLabel("Type").selectOption("Greenhouse");
  await page.getByLabel("Inside").selectOption({ label: "Croft" });
  await page.getByLabel("Name", { exact: true }).fill("Greenhouse 2");
  await button(page, "Add").click();
  await expect(
    page.locator(".card strong", { hasText: "Greenhouse 2" }),
  ).toBeVisible();
  await page.getByLabel("Type").selectOption("Bench");
  await page.getByLabel("Inside").selectOption({ label: "Greenhouse 2" });
  await page.getByLabel("Name", { exact: true }).fill("Bench 1");
  await button(page, "Add").click();
  await expect(
    page.locator(".card strong", { hasText: "Bench 1" }),
  ).toBeVisible();
  await leaveManage(page);

  // Staff, with a refused PIN first.
  await openManage(page, "Staff");
  await page.getByLabel("Name", { exact: true }).fill("Alex Moss");
  await page.getByLabel("PIN for the bench").fill("12");
  await button(page, "Add").click();
  await expect(page.getByRole("alert")).toHaveText(
    "A PIN must be 4 to 6 digits.",
  );
  await page.getByLabel("PIN for the bench").fill("4821");
  await button(page, "Add").click();
  await expect(page.getByText("Alex Moss", { exact: true })).toBeVisible();

  // An invitation opened on someone's own phone.
  await page
    .locator(".card", { hasText: "Alex Moss" })
    .getByRole("button", { name: "Phone invitation" })
    .click();
  const link = await page.getByTestId("invite-link").innerText();

  const phone = await browser.newContext();
  const phonePage = await phone.newPage();
  await addPasskeySupport(phonePage);
  await phonePage.goto(link);
  await button(phonePage, "Set up my passkey").click();
  await expect(
    phonePage.getByRole("heading", { name: "Hello, Alex Moss" }),
  ).toBeVisible();
  await expect(button(phonePage, "Manage")).toHaveCount(0);
  expect(new URL(phonePage.url()).search).toBe("");

  await phonePage.goto(link);
  await button(phonePage, "Set up my passkey").click();
  await expect(phonePage.getByRole("alert")).toContainText(
    "expired or been used",
  );
  await phone.close();

  await leaveManage(page);

  // This device becomes a bench station, which signs the owner out of it.
  await openManage(page, "Bench stations");
  await page.getByLabel("Name for this device").fill("Potting shed");
  await button(page, "Make this device a bench station").click();
  await expect(
    page.getByRole("heading", { name: "Who's working?" }),
  ).toBeVisible();

  // Tap a name, then the PIN: wrong first, then right.
  await button(page, "Alex Moss").click();
  for (const digit of "1111") {
    await button(page, digit).click();
  }
  await button(page, "Sign in").click();
  await expect(page.getByRole("alert")).toHaveText(
    "That PIN isn't right. Please try again.",
  );

  for (const digit of "4821") {
    await button(page, digit).click();
  }
  await button(page, "Sign in").click();
  await expect(
    page.getByRole("heading", { name: "Hello, Alex Moss" }),
  ).toBeVisible();

  // Finishing returns to the name list, not to sign-in.
  await button(page, "Finished").click();
  await expect(
    page.getByRole("heading", { name: "Who's working?" }),
  ).toBeVisible();
});
