import { expect, test as base } from "@playwright/test";

// Every test fails if the page throws an error nobody caught, even when the screen still looks right.
export const test = base.extend<{ pageErrors: void }>({
  pageErrors: [
    async ({ page }, use) => {
      const errors: string[] = [];
      page.on("pageerror", (error) => errors.push(error.message));
      await use();
      expect(errors, "errors thrown in the page").toEqual([]);
    },
    { auto: true },
  ],
});

export { expect };
