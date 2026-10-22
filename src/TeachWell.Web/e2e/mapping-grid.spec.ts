import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

// Uses DES 120 (the reports spec relies on WEB 101 starting without competencies).
test("instructor maps a whole module to a competency in the mapping grid", async ({ page, context }) => {
  await authenticate(context, "instructor");
  await page.goto("/");
  await page
    .getByRole("article")
    .filter({ has: page.getByRole("heading", { name: "Designing for Everyone" }) })
    .getByRole("button", { name: "Explore course" })
    .click();
  await page.getByRole("button", { name: "Expand Alignment and quality" }).click();

  await page.getByRole("button", { name: "Add competency" }).click();
  await page.getByLabel("Code").fill("COMP-GRID");
  await page.getByLabel("Title").fill("Design inclusive pages");
  await page.getByRole("button", { name: "Add competency" }).click();
  await expect(page.getByText("Competency added.")).toBeVisible();

  await page.getByRole("button", { name: "Map assessments" }).click();
  const grid = page.getByRole("region", { name: "Map assessments to competencies" });
  const cell = grid.getByRole("checkbox", { name: "Your first accessible page: COMP-GRID" });
  await expect(cell).not.toBeChecked();

  // The module row maps every assessment in the module; nothing is saved until Save.
  await grid.getByRole("checkbox", { name: "Map all of Start here to COMP-GRID" }).check();
  await expect(cell).toBeChecked();
  await grid.getByRole("button", { name: "Save 1 change" }).click();

  await expect(page.getByText("Saved 1 mapping change.")).toBeVisible();
  await expect(page.locator(".competency-list li").filter({ hasText: "COMP-GRID" })).toContainText("Your first accessible page");
});
