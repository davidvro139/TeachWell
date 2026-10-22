import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

// Uses its own department-scoped set and restores the course's classification afterwards, so later specs that
// depend on the college-wide defaults (task, support) see unchanged automated checks.
test("CTL configures a standard set, previews and publishes it, and classifies a course; the owner can only read", async ({
  browser,
  page,
  context,
}) => {
  page.on("dialog", (dialog) => dialog.accept());
  await authenticate(context, "reviewer");
  await page.goto("/");
  await page.getByRole("combobox", { name: "Workspace role" }).selectOption("CTLReviewer");
  await page.getByRole("button", { name: "Standards", exact: true }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Course standards" })).toBeVisible();

  // The seeded college-wide set is published and read-only.
  await page
    .getByRole("article")
    .filter({ has: page.getByRole("heading", { name: "College-wide course standards (demo)" }) })
    .getByRole("button", { name: /Version 1 · Published · 19 checks on/ })
    .click();
  await expect(page.getByText("This version can no longer change.", { exact: false })).toBeVisible();
  await expect(page.getByRole("button", { name: "Save draft" })).toHaveCount(0);

  await page.getByRole("textbox", { name: "Name" }).fill("Spec department standards");
  await page.getByRole("textbox", { name: "Department (optional)" }).fill("Spec Department");
  await page.getByRole("button", { name: "Create standard set" }).click();
  await expect(page.getByRole("heading", { name: "Spec department standards · Version 1" })).toBeVisible();
  await expect(page.getByText("Department: Spec Department", { exact: false }).first()).toBeVisible();

  const altCheck = page.locator("details.check-setting").filter({ hasText: "IMG_ALT_MISSING" });
  await altCheck.locator("summary").click();
  await altCheck.getByRole("checkbox", { name: "Run this check" }).uncheck();
  const vague = page.locator("details.check-setting").filter({ hasText: "LINK_VAGUE" });
  await vague.locator("summary").click();
  await vague.getByRole("combobox", { name: "Severity" }).selectOption("High");
  await expect(vague.getByRole("textbox", { name: /Why it matters/ })).not.toBeEmpty();
  // A new draft adopts the checks added since v1, including number thresholds.
  const content = page.getByRole("region", { name: "Content and cognitive load" });
  const reading = content.locator("details.check-setting").filter({ hasText: "READING_LEVEL_HIGH" });
  await reading.locator("summary").click();
  await expect(reading.getByRole("spinbutton", { name: /Highest acceptable grade level/ })).toHaveValue("14");
  await reading.getByRole("spinbutton", { name: /Highest acceptable grade level/ }).fill("12");
  await expect(page.getByText("Unsaved changes")).toBeVisible();
  await expect(page.getByRole("button", { name: "Publish version" })).toBeDisabled();
  await page.getByRole("button", { name: "Save draft" }).click();
  await expect(page.getByText("Draft saved.")).toBeVisible();

  await page.getByRole("combobox", { name: "Course", exact: true }).selectOption({ label: "WEB 101 · Foundations of Web Development" });
  await page.getByRole("button", { name: "Run preview" }).click();
  const preview = page.getByRole("table");
  await expect(preview.getByRole("rowheader", { name: /Image has no text alternative/ })).toContainText("(off)");

  await page.getByRole("button", { name: "Publish version" }).click();
  await expect(page.getByText("Version published.", { exact: false })).toBeVisible();
  await expect(page.getByRole("button", { name: "Save draft" })).toHaveCount(0);

  // Classify the course into the new set's department, then restore it.
  await page.getByRole("button", { name: "Courses", exact: true }).click();
  await page
    .getByRole("article")
    .filter({ has: page.getByRole("heading", { name: "Foundations of Web Development" }) })
    .getByRole("button", { name: "Explore course" })
    .click();
  const panel = page.getByRole("region", { name: "Course standards" });
  // A course setting, so it starts folded away (ADR 016).
  await panel.getByRole("button", { name: "Expand Course standards" }).click();
  await expect(panel).toContainText("College-wide course standards (demo)");
  await panel.getByRole("textbox", { name: "Department" }).fill("spec department");
  await panel.getByRole("button", { name: "Save classification" }).click();
  await expect(panel.getByText("New reviews of this course will use Spec department standards.", { exact: false })).toBeVisible();
  await expect(panel).toContainText("This set targets this course's department.");
  await panel.getByRole("textbox", { name: "Department" }).fill("");
  await panel.getByRole("button", { name: "Save classification" }).click();
  await expect(panel).toContainText("College-wide course standards (demo) (version 1)");

  // The owner sees which standards apply, but cannot classify and has no Standards tab.
  const ownerContext = await browser.newContext({ ignoreHTTPSErrors: true });
  await authenticate(ownerContext, "instructor");
  const owner = await ownerContext.newPage();
  await owner.goto("/");
  await expect(owner.getByRole("button", { name: "Standards", exact: true })).toHaveCount(0);
  await owner
    .getByRole("article")
    .filter({ has: owner.getByRole("heading", { name: "Foundations of Web Development" }) })
    .getByRole("button", { name: "Explore course" })
    .click();
  const ownerPanel = owner.getByRole("region", { name: "Course standards" });
  await ownerPanel.getByRole("button", { name: "Expand Course standards" }).click();
  await expect(ownerPanel).toContainText("College-wide course standards (demo)");
  await expect(ownerPanel.getByText("The CTL maintains this course's delivery mode and department", { exact: false })).toBeVisible();
  await expect(ownerPanel.getByRole("button", { name: "Save classification" })).toHaveCount(0);
  await ownerContext.close();
});
