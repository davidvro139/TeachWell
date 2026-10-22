import { expect, test } from "@playwright/test";
import { authenticate, forgetSession } from "./auth";

test("academic administrator sees results only; CTL re-checks courses and opens a shared one; system admin grants and removes a role", async ({
  browser,
  page,
  context,
}) => {
  // Academic administrator: oversight is the whole workspace, with no course content and nothing to change.
  await authenticate(context, "academic");
  await page.goto("/");
  await expect(page.getByRole("heading", { level: 1, name: "Course quality oversight" })).toBeVisible();
  // The sidebar offers oversight and nothing else.
  const sections = page.getByRole("navigation", { name: "Workspace sections" });
  await expect(sections.getByRole("button")).toHaveText(["Oversight"]);
  await expect(sections.getByRole("button", { name: "Oversight" })).toHaveAttribute("aria-current", "page");
  await expect(page.getByRole("button", { name: "Re-check all courses" })).toHaveCount(0);
  const rows = page.getByRole("region", { name: "Courses" }).getByRole("row");
  await expect(rows.filter({ hasText: "WEB 101" })).toBeVisible();
  await expect(rows.filter({ hasText: "WEB 101" }).getByRole("button")).toHaveCount(0);
  await expect(page.getByText("Figures describe courses, not instructors or reviewers.", { exact: false })).toHaveCount(1);
  await expect(page.getByLabel("Program")).toBeVisible();
  // The CSV download is a plain same-origin link carrying the session cookie.
  const download = page.getByRole("link", { name: "Download CSV" });
  await expect(download).toHaveAttribute("href", "/api/v1/oversight/export");
  const csv = await page.request.get("/api/v1/oversight/export");
  expect(csv.status()).toBe(200);
  expect(csv.headers()["content-type"]).toContain("text/csv");
  expect(await csv.text()).toContain("WEB 101");

  // CTL reviewer: re-check every course, then open a course shared with them from the table.
  const reviewerContext = await browser.newContext({ baseURL: "https://localhost:7443", ignoreHTTPSErrors: true });
  try {
    await authenticate(reviewerContext, "reviewer");
    const ctl = await reviewerContext.newPage();
    await ctl.goto("/");
    await ctl.getByRole("combobox", { name: "Workspace role" }).selectOption("CTLReviewer");
    await ctl.getByRole("button", { name: "Oversight", exact: true }).click();
    await ctl.getByRole("button", { name: "Re-check all courses" }).click();
    // Earlier specs may have imported more courses, so the count is not fixed.
    await expect(ctl.getByText(/Re-checked \d+ courses/)).toBeVisible();
    await expect(ctl.getByRole("heading", { name: "Most common issues" })).toBeVisible();
    const courses = ctl.getByRole("region", { name: "Courses" });
    // DES 120 is not shared with this reviewer: figures only, no way in.
    await expect(courses.getByRole("row").filter({ hasText: "DES 120" }).getByRole("button")).toHaveCount(0);
    await courses.getByRole("button", { name: /WEB 101/ }).click();
    await expect(ctl.getByRole("heading", { level: 2, name: "Improvement guide" })).toBeVisible();
  } finally {
    await reviewerContext.close();
  }

  // System administrator: grant and remove a role; both are audited (see OversightTests).
  const adminContext = await browser.newContext({ baseURL: "https://localhost:7443", ignoreHTTPSErrors: true });
  try {
    await authenticate(adminContext, "admin");
    const admin = await adminContext.newPage();
    await admin.goto("/");
    const toggle = admin.getByRole("checkbox", { name: "Academic administrator: Alex Morgan" });
    await expect(toggle).not.toBeChecked();
    // The box reflects the server's answer, so it changes after the request completes.
    await toggle.click();
    await expect(admin.getByText("Granted Academic administrator to Alex Morgan.")).toBeVisible();
    await expect(toggle).toBeChecked();
    await toggle.click();
    await expect(admin.getByText("Removed Academic administrator from Alex Morgan.")).toBeVisible();
    await expect(toggle).not.toBeChecked();
    await expect(admin.getByRole("checkbox", { name: "Instructor: Alex Morgan" })).toBeChecked();
    // Alex Morgan's cached session is now ending; later specs sign in again.
    forgetSession("instructor");
    // Both changes are in the audit trail, newest first, filterable by action.
    await admin.reload();
    const audit = admin.getByRole("region", { name: "Audit trail" });
    await audit.getByLabel("Action").selectOption({ label: "Role revoked: Academic administrator" });
    const row = audit.getByRole("row").filter({ hasText: "Role revoked: Academic administrator" });
    await expect(row).toHaveCount(1);
    await expect(row).toContainText("Sam Rivera");
    await expect(row).toContainText("Alex Morgan");
  } finally {
    await adminContext.close();
  }
});
