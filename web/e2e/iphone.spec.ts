import { expect, test } from "./fixtures";

// Staff mostly use iPhones. On a phone, a tray's Edit batch takes photos of the seed packet, label and packaging,
// with buttons big enough to tap and nothing running off the side of the screen.
test("Edit batch adds a seed packet photo on a phone", async ({ page }) => {
  const batchId = "0193a6f0-0000-7000-8000-0000000000b1";
  const photos: unknown[] = [];

  await page.route("**/api/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());

    if (url.pathname === `/api/batches/${batchId}/photos`) {
      if (request.method() === "POST") {
        const photo = {
          id: "0193a6f0-0000-7000-8000-0000000000f1",
          kind: url.searchParams.get("kind"),
          takenAt: "2026-11-13T10:00:00Z",
          day: 3,
          stage: "Sown",
          canRemove: true,
        };
        photos.push(photo);
        await route.fulfill({ status: 201, json: photo });
      } else {
        await route.fulfill({ json: photos });
      }
      return;
    }

    if (url.pathname.startsWith("/api/batch-photos/")) {
      await route.fulfill({
        contentType: "image/png",
        body: Buffer.from(
          "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
          "base64",
        ),
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
      "/api/containers/by-label/C000001": {
        id: "0193a6f0-0000-7000-8000-0000000000c1",
        labelCode: "C000001",
        kind: "Tray",
        contents: [
          {
            batchId,
            traceabilityCode: "B000001",
            plantId: "0193a6f0-0000-7000-8000-0000000000a1",
            commonName: "Forking Larkspur",
            variety: "King size Scarlett",
            quantity: 0,
            source: "SeedLot",
            notCounted: true,
            rough: false,
          },
        ],
        locationId: null,
        locationName: "Greenhouse 1",
        potSizeId: null,
        potSizeName: null,
        stage: "Sown",
        stageSince: "2026-11-10T09:00:00Z",
        startedAt: "2026-11-10T09:00:00Z",
      },
      [`/api/batches/${batchId}`]: {
        id: batchId,
        traceabilityCode: "B000001",
        plantId: "0193a6f0-0000-7000-8000-0000000000a1",
        source: "SeedLot",
        sourceReference: null,
        supplierId: null,
        motherPlant: null,
        originCountry: "GB",
        sowBy: null,
        sownAs: "Packet",
        sownAmount: null,
        packetShare: "Whole",
        packetSays: null,
        motherPlantId: null,
        startedAt: "2026-11-10T09:00:00Z",
        quantity: 0,
        startCanChange: true,
        receivedPassports: 0,
        splitInto: 0,
      },
      "/api/plants": [
        {
          id: "0193a6f0-0000-7000-8000-0000000000a1",
          commonName: "Forking Larkspur",
          variety: "King size Scarlett",
          archived: false,
          prices: [],
        },
      ],
    };
    await route.fulfill({
      json: Object.hasOwn(data, url.pathname) ? data[url.pathname] : [],
    });
  });

  await page.goto("/");
  await page.getByLabel("Scan a label").fill("C000001");
  await page.getByLabel("Scan a label").press("Enter");
  await expect(page.getByRole("heading", { name: "C000001" })).toBeVisible();
  await page.getByRole("button", { name: "Edit batch" }).click();

  const add = page.getByRole("group", { name: "Add a photo" });
  await expect(add).toBeVisible();
  for (const kind of ["Seed packet", "Label", "Packaging"]) {
    const choice = add.locator(".photo-choice", { hasText: kind });
    await expect(choice).toBeVisible();
    expect((await choice.boundingBox())!.height).toBeGreaterThanOrEqual(44);
  }

  await add
    .locator(".photo-choice", { hasText: "Seed packet" })
    .locator("input[type=file]")
    .setInputFiles({
      name: "packet.png",
      mimeType: "image/png",
      buffer: Buffer.from(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
        "base64",
      ),
    });
  await expect.poll(() => photos.length).toBe(1);
  expect((photos[0] as { kind: string }).kind).toBe("Packet");
  await expect(
    page.getByRole("list", { name: "Photos of this batch" }).locator("img"),
  ).toHaveCount(1);

  expect(
    await page.evaluate(
      () => document.documentElement.scrollWidth <= innerWidth,
    ),
  ).toBe(true);
});
