import { expect, test } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { authenticate } from "./auth";
import type { CanvasConnection } from "../src/api";

test("disabled Canvas leaves the offline library usable", async ({
  page,
  context,
}) => {
  await authenticate(context, "instructor");
  await page.goto("/");
  await page
    .getByRole("button", { name: "Import courses", exact: true })
    .click();
  await expect(
    page.getByRole("heading", {
      name: "Canvas is not enabled for this workspace",
    }),
  ).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "No Canvas connections yet" }),
  ).toBeVisible();
  await expect(page.getByLabel("Personal access token")).toHaveCount(0);
  await page.getByRole("button", { name: "Courses", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Foundations of Web Development" }),
  ).toBeVisible();
});

test("Canvas form clears submitted credentials and displays failure, validation and disconnect states (mock HTTP)", async ({
  page,
  context,
}) => {
  await authenticate(context, "instructor");
  let saved: CanvasConnection | null = null;
  let reject = true;
  const fictionalToken = randomUUID();
  // Explicit UI-only fixtures. Backend lifecycle/crypto tests exercise the actual service.
  await page.route("**/api/v1/canvas/**", async (route) => {
    const request = route.request();
    const url = new URL(request.url());
    if (url.pathname.endsWith("/settings"))
      return route.fulfill({
        json: {
          enabled: true,
          allowedOrigins: ["https://canvas.example.test/"],
        },
      });
    if (request.method() === "GET")
      return route.fulfill({ json: saved ? [saved] : [] });
    if (request.method() === "DELETE") {
      saved = {
        ...saved!,
        status: "Disconnected",
        tokenMask: null,
        canvasUserId: null,
        errorCode: null,
      };
      return route.fulfill({ status: 204 });
    }
    if (url.pathname.endsWith("/validate")) {
      saved = {
        ...saved!,
        status: "Invalid",
        tokenMask: null,
        errorCode: "InvalidToken",
      };
      return route.fulfill({
        status: 422,
        json: {
          title:
            "Canvas rejected the token. It may have expired or been revoked.",
        },
      });
    }
    expect(request.postDataJSON().token).toBe(fictionalToken);
    if (reject) {
      reject = false;
      return route.fulfill({
        status: 422,
        json: {
          title:
            "Canvas rejected the token. It may have expired or been revoked.",
        },
      });
    }
    saved = {
      id: randomUUID(),
      baseUrl: "https://canvas.example.test/",
      status: "Connected",
      canvasUserId: "7654321",
      tokenMask: "Saved token",
      expiresAt: null,
      lastValidatedAt: new Date().toISOString(),
      lastCheckedAt: new Date().toISOString(),
      errorCode: null,
    };
    return route.fulfill({ json: saved });
  });
  await page.goto("/");
  await page
    .getByRole("button", { name: "Import courses", exact: true })
    .click();
  const input = page.getByLabel("Personal access token", { exact: true });
  await expect(input).toHaveAttribute("type", "password");
  await input.fill(fictionalToken);
  await page.getByRole("button", { name: "Validate and save" }).click();
  await expect(page.getByRole("alert")).toContainText(
    "Canvas rejected the token",
  );
  await expect(input).toHaveValue("");
  await input.fill(fictionalToken);
  await page.getByRole("button", { name: "Validate and save" }).click();
  await expect(
    page.getByText("Saved securely", { exact: false }),
  ).toBeVisible();
  await expect(input).toHaveValue("");
  await expect(page.getByText(fictionalToken, { exact: true })).toHaveCount(0);
  await page
    .getByRole("button", { name: "Validate connection", exact: true })
    .click();
  await expect(page.getByRole("alert")).toContainText(
    "Canvas rejected the token",
  );
  await expect(page.getByText("No saved token", { exact: true })).toBeVisible();
  await page.getByRole("button", { name: "Disconnect", exact: true }).click();
  await expect(
    page.getByText("TeachWell removed its saved token", { exact: false }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Disconnect", exact: true }),
  ).toBeDisabled();
});
