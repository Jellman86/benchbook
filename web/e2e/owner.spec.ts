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

// A drop-down's accessible name includes its options, so find it by the label's start.
const chooser = (page: Page, name: string) =>
  page
    .locator("label")
    .filter({ hasText: new RegExp(`^\\s*${name}`) })
    .locator("select");

async function openManage(page: Page, section: string) {
  await button(page, "Manage").click();
  await button(page, section).click();
  await expect(page.getByRole("heading", { name: section })).toBeVisible();

  // On a phone, long forms wait behind a button.
  const showForm = page.locator(".panel-toggle");
  if (await showForm.isVisible()) {
    await showForm.click();
  }
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
  await button(page, "More").click();
  await button(page, "Sign out").click();
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
  await expect(page.getByText("Not counted yet")).toBeVisible();
  await page.getByLabel("Add photo").setInputFiles({
    name: "pot.png",
    mimeType: "image/png",
    buffer: Buffer.from(
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
      "base64",
    ),
  });
  await expect(page.getByAltText("The pot")).toBeVisible();
  await button(page, "Count").click();
  await page.getByLabel("How many we've got", { exact: true }).fill("120");
  await button(page, "Save count").click();
  await expect(page.getByText("120 in stock")).toBeVisible();
  await page.getByLabel("New pot size").fill("9CM");
  await button(page, "Add").click();
  await expect(page.getByRole("alert")).toHaveText(
    "That pot size is already on the list.",
  );
  await leaveManage(page);

  // Plants start from a search, and fall back to typing them in.
  await openManage(page, "Plants");
  await page.getByLabel("Find or add a plant").fill("Lavender");
  await button(page, "Not listed? Add it by hand").click();
  await page.getByLabel("Variety (if there is one)").fill("Hidcote");
  await page
    .getByLabel("Botanical name (if you know it)")
    .fill("Lavandula angustifolia");
  await button(page, "Next").click();
  await page.getByLabel("Price for 9cm (£)").fill("4.50");
  await button(page, "Add").click();
  await expect(page.getByText("Lavender 'Hidcote'")).toBeVisible();

  // The plant's card holds what we know, and growing notes anyone can add to.
  await page.locator(".plant-card", { hasText: "Lavender 'Hidcote'" }).click();
  await page.getByLabel("Add a growing note").fill("Cut back hard in August.");
  await button(page, "Add note").click();
  await expect(page.getByText("Cut back hard in August.")).toBeVisible();
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

  // Money out: a purchase with its VAT, then paying it.
  await openManage(page, "Purchases");
  await chooser(page, "Supplier").selectOption({ label: "Hillside Plants" });
  await page.getByLabel("Invoice reference").fill("INV-204");
  await page.getByLabel("Item").fill("Compost");
  await page.getByLabel("Cost before VAT (£)").fill("45.50");
  await page.getByLabel("VAT (£)", { exact: true }).fill("9.10");
  await button(page, "Save purchase").click();
  await expect(page.getByText("Hillside Plants: £54.60")).toBeVisible();
  await expect(page.getByText("Still owed: £54.60")).toBeVisible();
  await page.getByLabel("Add a receipt").setInputFiles({
    name: "receipt.png",
    mimeType: "image/png",
    buffer: Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 1]),
  });
  await expect(page.getByRole("link", { name: "Receipt 1" })).toBeVisible();
  await button(page, "Mark paid").click();
  await expect(page.getByText("Still owed: £0.00")).toBeVisible();
  await leaveManage(page);

  // Money in: a day's takings show in the month's figures, with the purchase.
  await openManage(page, "Money");
  await page.getByLabel("Amount taken (£)").fill("123.45");
  await button(page, "Save takings").click();
  await expect(page.getByText("Income: £123.45")).toBeVisible();
  await expect(page.getByText("Spending: £54.60")).toBeVisible();
  await expect(page.getByText("Still owed to suppliers: £0.00")).toBeVisible();
  await page.getByLabel("Amount taken (£)").fill("10");
  await button(page, "Save takings").click();
  await expect(page.getByRole("alert")).toContainText("already entered");
  await leaveManage(page);

  // Figures open, with nothing recorded yet.
  await openManage(page, "Figures");
  await expect(page.getByText("Nothing recorded yet.")).toBeVisible();
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

  await leaveManage(page);

  // A desktop screen gets a sidebar with the same sections and no Back buttons.
  await page.setViewportSize({ width: 1280, height: 800 });
  const sidebar = page.getByRole("navigation", { name: "Main" });
  await expect(sidebar).toBeVisible();
  await sidebar.getByRole("button", { name: "Plants" }).click();
  await expect(page.getByRole("heading", { name: "Plants" })).toBeVisible();
  await expect(button(page, "Back")).toHaveCount(0);
  await sidebar.getByRole("button", { name: "Figures" }).click();
  await expect(page.getByRole("heading", { name: "Figures" })).toBeVisible();
  await sidebar.getByRole("button", { name: "Record work" }).click();
  await expect(page.getByLabel("Scan a label")).toBeFocused();

  // The sidebar folds down to icons, and stays that way after a reload.
  const rail = page.locator("aside.sidebar");
  await page.getByRole("button", { name: "Collapse sidebar" }).click();
  await expect
    .poll(() => rail.evaluate((node) => node.getBoundingClientRect().width))
    .toBeLessThan(100);
  await page.reload();
  await expect(
    page.getByRole("button", { name: "Expand sidebar" }),
  ).toBeVisible();

  // The theme can be forced either way, or left to the device.
  const root = page.locator("html");
  await page.getByRole("button", { name: "Theme: Match device" }).click();
  await expect(root).toHaveAttribute("data-theme", "light");
  await page.getByRole("button", { name: "Theme: Light" }).click();
  await expect(root).toHaveAttribute("data-theme", "dark");
  await sidebar.getByRole("button", { name: "Plants" }).click();
  await expect(page.getByText("Lavender 'Munstead'")).toBeVisible();
  await page.getByRole("button", { name: "Expand sidebar" }).click();
  await expect
    .poll(() => rail.evaluate((node) => node.getBoundingClientRect().width))
    .toBeGreaterThan(200);
  await page.reload();
  await expect(root).toHaveAttribute("data-theme", "dark");

  // On a phone the sidebar gives way to a bar along the bottom.
  await page.setViewportSize({ width: 420, height: 900 });
  await expect(sidebar).toBeHidden();
  await button(page, "Manage").click();
  await button(page, "Plants").click();
  await expect(page.getByText("Lavender 'Munstead'")).toBeVisible();
  await expect(page.getByLabel("Find or add a plant")).toBeVisible();
  await button(page, "Back").click();
  await button(page, "More").click();
  await expect(page.getByRole("heading", { name: "Appearance" })).toBeVisible();
  await button(page, "Match device").click();
  await expect(root).not.toHaveAttribute("data-theme");
  await button(page, "Record").click();
  await expect(page.getByLabel("Scan a label")).toBeVisible();

  // The owner adds a password in settings, alongside the passkey.
  await button(page, "More").click();
  await page.getByLabel("New password").fill("correct horse battery");
  await button(page, "Set a password").click();
  await expect(page.getByRole("status")).toHaveText("Password saved.");

  // Once the owner allows it, someone added with just a name can sign in by tapping it.
  await openManage(page, "Staff");
  await expect(chooser(page, "How they sign in")).not.toContainText(
    "Just tap their name",
  );
  await page.getByLabel("Let staff sign in by tapping their name").check();
  await page.getByLabel("Name", { exact: true }).fill("Jo Reed");
  await chooser(page, "How they sign in").selectOption("name");
  await button(page, "Add").click();
  await expect(page.getByText("Signs in by tapping their name")).toBeVisible();
  await leaveManage(page);
  await button(page, "More").click();
  await button(page, "Sign out").click();
  await button(page, "Jo Reed").click();
  await expect(
    page.getByRole("heading", { name: "Hello, Jo Reed" }),
  ).toBeVisible();
  await expect(button(page, "Manage")).toHaveCount(0);

  // The owner has a password, so tapping their name asks for it.
  await button(page, "More").click();
  await button(page, "Sign out").click();
  await button(page, "Sam Fielding").click();
  await page.getByLabel("Password", { exact: true }).fill("not the one");
  await button(page, "Sign in").click();
  await expect(page.getByRole("alert")).toHaveText(
    "That password isn't right.",
  );
  await page
    .getByLabel("Password", { exact: true })
    .fill("correct horse battery");
  await button(page, "Sign in").click();
  await expect(
    page.getByRole("heading", { name: "Hello, Sam Fielding" }),
  ).toBeVisible();

  // The owner can set up a shared staff login, and bad settings are refused.
  await openManage(page, "Staff login");
  await expect(page.getByTestId("redirect-uri")).toContainText("/signin-oidc");
  await page.getByLabel("Offer this on the sign-in page").check();
  await page.getByLabel("Provider address").fill("http://not-secure.test");
  await page.getByLabel("Client ID").fill("growlog");
  await button(page, "Save").click();
  await expect(page.getByRole("alert")).toContainText("https address");
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

  // Start a batch.
  await button(page, "New batch").click();
  await page.locator(".plant-card", { hasText: "Lavender 'Munstead'" }).click();
  await button(page, "40").click();
  await chooser(page, "Pot size").selectOption({ label: "9cm" });
  await button(page, "Next").click();
  await chooser(page, "Supplier").selectOption({ label: "Hillside Plants" });
  await button(page, "Next").click();
  await page.getByLabel("Seed packet").setInputFiles({
    name: "packet.png",
    mimeType: "image/png",
    buffer: Buffer.from(
      "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
      "base64",
    ),
  });
  await expect(button(page, "Next")).toBeVisible();
  await button(page, "Next").click();
  await chooser(page, "Place").selectOption({ label: "Greenhouse 2, Bench 1" });
  await button(page, "Start batch").click();
  const started = page.getByRole("status");
  await expect(started).toContainText("Started 40 Lavender 'Munstead'");
  const label = /Label: (C\d+)/.exec((await started.innerText()) ?? "")![1];

  // A new batch offers its label, and it really is a PDF.
  const pdf = await page.request.get(
    (await page
      .getByRole("link", { name: "Print label" })
      .getAttribute("href"))!,
  );
  expect(pdf.status()).toBe(200);
  expect((await pdf.body()).subarray(0, 4).toString()).toBe("%PDF");

  // The scan box is ready straight away, as a USB scanner needs it to be.
  await expect(page.getByLabel("Scan a label")).toBeFocused();

  async function scan(code: string) {
    await page.getByLabel("Scan a label").fill(code);
    await page.getByLabel("Scan a label").press("Enter");
  }

  // Scan, Lost, how many, why. Then undo it.
  await scan(label);
  await expect(page.getByRole("heading", { name: label })).toBeVisible();
  await expect(page.getByText("40 Lavender 'Munstead'")).toBeVisible();
  await expect(
    page.getByText("Greenhouse 2, Bench 1").or(page.getByText("Bench 1, 9cm")),
  ).toBeVisible();
  await button(page, "Lost").click();
  await button(page, "+1").click();
  await button(page, "+1").click();
  await expect(page.getByTestId("quantity")).toHaveText("3");
  await button(page, "Pest").click();
  await expect(page.getByRole("status")).toContainText(
    "Lost 3 Lavender 'Munstead' (Pest)",
  );

  await scan(label);
  await expect(page.getByText("37 Lavender 'Munstead'")).toBeVisible();
  await button(page, "Back").click();

  await scan(label);
  await button(page, "Lost").click();
  await button(page, "Frost").click();
  await page.getByRole("status").getByRole("button", { name: "Undo" }).click();
  await expect(page.getByRole("status")).toHaveCount(0);
  await scan(label);
  await expect(page.getByText("37 Lavender 'Munstead'")).toBeVisible();

  // More than are there can't be lost: the stepper stops at what's there.
  await button(page, "Lost").click();
  await button(page, "+10").click();
  await button(page, "+10").click();
  await button(page, "+10").click();
  await button(page, "+10").click();
  await expect(page.getByTestId("quantity")).toHaveText("37");
  await button(page, "Back").click();

  // Move it.
  await button(page, "Move").click();
  await button(page, "Greenhouse 2").click();
  await expect(page.getByRole("status")).toContainText(
    `Moved ${label} to Greenhouse 2`,
  );

  // Pot on part of it, which makes a new label. It starts at the whole tray.
  await scan(label);
  await button(page, "Pot on").click();
  await expect(page.getByTestId("quantity")).toHaveText("37");
  await button(page, "Type a number instead").click();
  await page.getByLabel("How many", { exact: true }).fill("11");
  await page.getByLabel("How many", { exact: true }).press("Tab");
  await expect(page.getByTestId("quantity")).toHaveText("11");
  await button(page, "Done").click();
  await expect(page.getByRole("status")).toContainText("Potted on 11");
  const newLabel = /New label: (C\d+)/.exec(
    await page.getByRole("status").innerText(),
  )![1];
  expect(newLabel).not.toBe(label);

  await scan(newLabel);
  await expect(page.getByText("11 Lavender 'Munstead'")).toBeVisible();
  await button(page, "Back").click();

  // Count what's left.
  await scan(label);
  await expect(page.getByText("26 Lavender 'Munstead'")).toBeVisible();
  await button(page, "Count").click();
  await expect(page.getByTestId("quantity")).toHaveText("26");
  await button(page, "−1").click();
  await button(page, "Done").click();
  await expect(page.getByRole("status")).toContainText("Counted 25");

  // A treatment, then a note.
  await scan(label);
  await button(page, "Treated").click();
  await page.getByLabel("Product").fill("Neem oil");
  await page.getByLabel("Dose").fill("5ml per litre");
  await page.getByLabel("Area or crop treated").fill("Tray");
  await button(page, "Done").click();
  await expect(page.getByRole("status")).toContainText("with Neem oil");

  await scan(label);
  await button(page, "Note").click();
  await page.getByLabel("Note", { exact: true }).fill("Looking strong");
  await button(page, "Done").click();
  await expect(page.getByRole("status")).toContainText("Note added");

  // A received passport, filled in from what's known and copied from the label.
  await scan(label);
  await button(page, "Passports").click();
  await expect(page.getByText("No passports recorded")).toBeVisible();
  await button(page, "Add a passport").click();
  await expect(page.getByLabel("A: Botanical name")).toHaveValue(
    "Lavandula angustifolia",
  );
  await expect(page.getByLabel("D: Origin")).toHaveValue("GB");
  await page.getByLabel("B: Registration number").fill("12345");
  await page.getByLabel("C: Traceability code").fill("LOT-77");
  await button(page, "Save passport").click();
  await expect(page.getByText("B: 12345")).toBeVisible();
  await expect(page.getByText("potted on since")).toBeVisible();
  await button(page, "Used up").click();
  await expect(
    page.locator(".card strong", { hasText: "Used up" }),
  ).toBeVisible();
  await button(page, "Back").click();
  await button(page, "Back").click();

  // An unknown label says so and goes back to scanning.
  await scan("NOPE");
  await expect(page.getByRole("alert")).toContainText(
    "No container has the label NOPE",
  );

  // One press repeats a recent action.
  await page
    .locator(".card", { hasText: "Sowed 40 Lavender 'Munstead'" })
    .getByRole("button", { name: "Again" })
    .click();
  await expect(page.getByRole("status")).toContainText("Again: Sowed 40");
  const repeated = /Label: (C\d+)/.exec(
    await page.getByRole("status").innerText(),
  )![1];
  expect(repeated).not.toBe(label);

  // Marking a tray ready moves it along in the production view.
  await scan(label);
  await button(page, "Ready").click();
  await expect(page.getByRole("status")).toContainText(`${label} marked ready`);
  await button(page, "Production").click();
  await expect(page.getByRole("heading", { name: "Production" })).toBeVisible();
  const trayRow = page.locator(".production-row", { hasText: label });
  await expect(trayRow).toContainText("Ready");
  await page.locator(".chips button", { hasText: "Sown" }).click();
  await expect(trayRow).toHaveCount(0);
  await page.locator(".chips button", { hasText: "Ready" }).click();
  await trayRow.click();
  await expect(page.getByRole("heading", { name: label })).toBeVisible();
  await button(page, "Back").click();
  await expect(page.getByRole("heading", { name: "Production" })).toBeVisible();
  await button(page, "Back").click();

  // A plant not on any list can be added by hand, in the same steps.
  await button(page, "New batch").click();
  await page.getByLabel("What are you planting?").fill("Cosmos");
  await button(page, "Not listed? Add it by hand").click();
  await expect(page.getByLabel("Common name")).toHaveValue("Cosmos");
  await button(page, "Next").click();
  await button(page, "Next").click();
  await button(page, "Next").click();
  await button(page, "Skip photos").click();
  await button(page, "Start batch").click();
  await expect(page.getByRole("status")).toContainText("Started 1 Cosmos");

  // On someone's own phone with no signal: the app still opens and work waits to send.
  await phonePage.evaluate(() => navigator.serviceWorker.ready);
  await phonePage.goto("/");
  await expect(
    phonePage.getByRole("heading", { name: "Hello, Alex Moss" }),
  ).toBeVisible();
  await phonePage.waitForFunction(() =>
    (localStorage.getItem("growlog.containers") ?? "").includes("C"),
  );

  await phone.setOffline(true);
  await phonePage.reload();
  await expect(
    phonePage.getByRole("heading", { name: "Hello, Alex Moss" }),
  ).toBeVisible();
  await expect(phonePage.getByTestId("offline")).toBeVisible();

  await phonePage.getByLabel("Scan a label").fill(label);
  await phonePage.getByLabel("Scan a label").press("Enter");
  await expect(phonePage.getByText("25 Lavender 'Munstead'")).toBeVisible();
  await button(phonePage, "Lost").click();
  await button(phonePage, "Pest").click();
  await expect(phonePage.getByRole("status")).toContainText(
    "Saved on this device",
  );
  await expect(phonePage.getByTestId("waiting")).toContainText(
    "1 thing waiting",
  );

  // Waiting work survives the app being closed and opened again.
  await phonePage.reload();
  await expect(phonePage.getByTestId("waiting")).toContainText(
    "1 thing waiting",
  );

  // Back in signal, it sends by itself.
  await phone.setOffline(false);
  await expect(phonePage.getByTestId("waiting")).toHaveCount(0, {
    timeout: 40_000,
  });
  await phonePage.getByLabel("Scan a label").fill(label);
  await phonePage.getByLabel("Scan a label").press("Enter");
  await expect(phonePage.getByText("24 Lavender 'Munstead'")).toBeVisible();
  await phone.close();

  // Finishing returns to the name list, not to sign-in.
  await button(page, "Finished").click();
  await expect(
    page.getByRole("heading", { name: "Who's working?" }),
  ).toBeVisible();
});
