import { resolve } from "node:path";
import { defineConfig, devices } from "@playwright/test";

const port = 5199;
const databasePort = 55432;
const postgres =
  "postgres:18.6-alpine@sha256:123b17998ccaf47e53b502e8c6a1e79afd8f579c6f3eec83c563da026f957395";

// Runs the real application and a throwaway database. http://localhost counts as secure for passkeys.
export default defineConfig({
  testDir: "e2e",
  workers: 1,
  forbidOnly: !!process.env.CI,
  // One long story that grows with GrowLog, including a phone waiting for its signal back.
  timeout: 120_000,
  use: { baseURL: `http://localhost:${port}` },
  projects: [
    {
      // The whole story, phone-sized. Passkeys use Chromium's virtual authenticator, so this one runs in Chromium.
      name: "chromium",
      use: {
        ...devices["Desktop Chrome"],
        viewport: { width: 390, height: 900 },
      },
    },
    {
      // Staff mostly use iPhones: the phone screens again in Safari's engine, at an iPhone's size and touch. Where
      // WebKit can't run locally, PW_WEBKIT_WS points at a Playwright server in its official Docker image.
      name: "iphone",
      testMatch: /(iphone|ask|review-phone)\.spec\.ts$/,
      use: {
        ...devices["iPhone 15"],
        // The app's service worker would answer before the tests' stand-in server could.
        serviceWorkers: "block",
        ...(process.env.PW_WEBKIT_WS
          ? { connectOptions: { wsEndpoint: process.env.PW_WEBKIT_WS } }
          : {}),
      },
    },
  ],
  webServer: [
    {
      command: `docker run --rm --name growlog-e2e -e POSTGRES_PASSWORD=e2e -p ${databasePort}:5432 ${postgres}`,
      port: databasePort,
      timeout: 180_000,
      gracefulShutdown: { signal: "SIGTERM", timeout: 10_000 },
    },
    {
      command:
        "dotnet run --project ../src/GrowLog.Api --configuration Release --no-launch-profile",
      url: `http://localhost:${port}/api/health`,
      timeout: 240_000,
      env: {
        ASPNETCORE_URLS: `http://localhost:${port}`,
        ASPNETCORE_WEBROOT: resolve("dist"),
        ConnectionStrings__GrowLog: `Host=localhost;Port=${databasePort};Username=postgres;Password=e2e;Database=postgres`,
        Passkeys__ServerDomain: "localhost",
        Passkeys__Origin: `http://localhost:${port}`,
        Setup__Token: "e2e-setup-code",
        Species__Online: process.env.E2E_SPECIES_ONLINE ?? "false",
      },
    },
  ],
});
