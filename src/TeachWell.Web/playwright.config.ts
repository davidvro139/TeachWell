import { defineConfig } from "@playwright/test";

export default defineConfig({
  testDir: "./e2e",
  workers: 1,
  retries: 0,
  reporter: "list",
  use: {
    baseURL: "https://localhost:7443",
    channel: "msedge",
    ignoreHTTPSErrors: true,
    // Authenticated pages and credentials must not be recorded in artifacts.
    screenshot: "off",
    video: "off",
    trace: "off",
  },
});
