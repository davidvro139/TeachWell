import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

test("instructor maps a competency to an assignment and sees full coverage; CTL reads the same report and the CTL overview", async ({
  browser,
  page,
  context,
}) => {
  await authenticate(context, "instructor");
  await page.goto("/");
  await page
    .getByRole("article")
    .filter({ has: page.getByRole("heading", { name: "Foundations of Web Development" }) })
    .getByRole("button", { name: "Explore course" })
    .click();
  await expect(page.getByRole("heading", { name: "Alignment and quality" })).toBeVisible();
  // The section starts collapsed on the course page; its summary line stays visible.
  await page.getByRole("button", { name: "Expand Alignment and quality" }).click();

  // Before any mapping, the one assignment is unmapped and there is no competency yet.
  await expect(page.getByText("No competencies recorded for this course yet, so coverage cannot be measured.")).toBeVisible();

  await page.getByRole("button", { name: "Add competency", exact: true }).click();
  await page.getByLabel("Code", { exact: true }).fill("co-1");
  await page.getByLabel("Title", { exact: true }).fill("Explain accessible design choices");
  await page.getByRole("button", { name: "Add competency", exact: true }).click();
  await expect(page.getByText("Competency added.")).toBeVisible();
  await expect(page.getByText("No assessment evidence mapped yet")).toBeVisible();
  await page.getByText("Not yet mapped to a competency (1 assessment)").click();
  await expect(page.locator(".name-list li", { hasText: "Your first accessible page" })).toBeVisible();

  await page.getByRole("combobox", { name: "Map an assignment to Explain accessible design choices" }).selectOption({ label: "Your first accessible page" });
  await page.getByRole("button", { name: "Map", exact: true }).click();
  await expect(page.getByText("Mapped.")).toBeVisible();
  await expect(page.getByRole("button", { name: "Unmap Your first accessible page" })).toBeVisible();
  const coverage = page.locator(".report-grid").first();
  await expect(coverage).toContainText("100%");
  await expect(coverage).toContainText("1 of 1 assessments mapped");

  // Owner's own reports tab shows only their revision tasks, never the CTL-only overview.
  await page.getByRole("button", { name: "Reports", exact: true }).click();
  await expect(page.getByRole("heading", { name: "Your revision tasks" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "CTL overview" })).toHaveCount(0);

  const reviewerContext = await browser.newContext({ baseURL: "https://localhost:7443", ignoreHTTPSErrors: true });
  try {
    await authenticate(reviewerContext, "reviewer");
    const reviewer = await reviewerContext.newPage();
    await reviewer.goto("/");
    await reviewer.getByRole("combobox", { name: "Workspace role" }).selectOption("CTLReviewer");
    await reviewer
      .getByRole("article")
      .filter({ has: reviewer.getByRole("heading", { name: "Foundations of Web Development" }) })
      .getByRole("button", { name: "Explore course" })
      .click();
    await reviewer.getByRole("button", { name: "Expand Alignment and quality" }).click();
    await expect(reviewer.getByText("Explain accessible design choices")).toBeVisible();
    const reviewerCoverage = reviewer.locator(".report-grid").first();
    await expect(reviewerCoverage).toContainText("100%");
    // Read-only for CTL: no authoring controls on a course they do not own.
    await expect(reviewer.getByRole("button", { name: "Add competency" })).toHaveCount(0);
    await expect(reviewer.getByRole("button", { name: /^Unmap / })).toHaveCount(0);

    await reviewer.getByRole("button", { name: "Reports", exact: true }).click();
    await expect(reviewer.getByRole("heading", { name: "CTL overview" })).toBeVisible();
    await expect(reviewer.getByText("Accessible courses")).toBeVisible();
    await expect(reviewer.getByRole("heading", { name: "Your revision tasks" })).toBeVisible();
  } finally {
    await reviewerContext.close();
  }
});
