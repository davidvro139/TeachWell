import { fileURLToPath } from "node:url";
import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

test("instructor uploads a course export, sees the server's size limit and the import result", async ({ page, context }) => {
  await authenticate(context, "instructor");
  await page.goto("/");
  await page.getByRole("button", { name: "Import courses", exact: true }).click();
  const panel = page.getByRole("region", { name: "Import a course export" });
  // The limit comes from the server (CourseExports:MaxUploadMegabytes, default 500).
  await expect(panel.getByText("Course export (.imscc, up to 500 MB)")).toBeVisible();

  await panel.getByLabel(/Course export/).setInputFiles(fileURLToPath(new URL("../../../samples/fictional-course-export.imscc", import.meta.url)));
  await expect(panel.getByText(/Selected: [\d.]+ MB/)).toBeVisible();
  await panel.getByRole("button", { name: "Upload and import" }).click();
  await expect(panel.getByText(/^Imported Introductory Biology/)).toBeVisible({ timeout: 15000 });
  await expect(panel.getByText("Your file is deleted as soon as the import finishes", { exact: false })).toBeVisible();
  await expect(panel.getByRole("button", { name: "Open course" })).toBeVisible();
});
