import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

test("instructor uses the improvement guide without a reviewer: reads guidance, asks for help, sets a step aside with a reason; the CTL sees why", async ({
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

  await expect(page.getByRole("heading", { level: 2, name: "Improvement guide" })).toBeVisible();
  await expect(page.getByRole("group", { name: "Areas of the course" }).getByRole("button")).toHaveCount(5);

  // An area explains why it matters.
  await page.getByRole("button", { name: /Outcomes and alignment/ }).click();
  await expect(page.getByRole("heading", { name: "Why outcomes and alignment matters" })).toBeVisible();

  const step = page.getByRole("button", { name: /No course competencies are defined/ });
  await step.click();
  await expect(step).toHaveAttribute("aria-expanded", "true");
  await expect(page.getByRole("heading", { name: "Why it matters" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "How to fix it" })).toBeVisible();
  await expect(page.getByText("Whole course", { exact: true })).toBeVisible();

  await page.getByRole("button", { name: "Ask the CTL for help" }).click();
  await page.getByLabel("What would you like help with?").fill("Our outcomes are in Simple Syllabus; how should I record them?");
  await page.getByRole("button", { name: "Send request" }).click();
  await expect(page.getByText("Your request was sent to the CTL")).toBeVisible();
  await page.getByRole("button", { name: "Close", exact: true }).click();

  await page.getByRole("button", { name: "Not applicable…" }).click();
  await page.getByLabel(/Why doesn't "No course competencies are defined" apply/).fill("Recorded in Simple Syllabus until we paste them in.");
  await page.getByRole("button", { name: "Save reason" }).click();
  await expect(page.getByText('Set aside "No course competencies are defined".')).toBeVisible();
  await expect(step).toHaveCount(0);

  await page.getByText(/Done or set aside/).click();
  await expect(page.getByText("Recorded in Simple Syllabus until we paste them in.", { exact: false })).toBeVisible();

  // Slice 6: the CTL sees which checks instructors set aside, and why (WEB 101 is shared with this reviewer).
  const reviewerContext = await browser.newContext({ baseURL: "https://localhost:7443", ignoreHTTPSErrors: true });
  try {
    await authenticate(reviewerContext, "reviewer");
    const ctl = await reviewerContext.newPage();
    await ctl.goto("/");
    await ctl.getByRole("combobox", { name: "Workspace role" }).selectOption("CTLReviewer");
    await ctl.getByRole("button", { name: "Standards", exact: true }).click();
    const feedback = ctl.getByRole("region", { name: "How instructors respond to checks" });
    const row = feedback.getByRole("row").filter({ hasText: "No course competencies are defined" });
    await expect(row).toBeVisible();
    await row.getByText(/^Reasons/).click();
    await expect(row).toContainText("Recorded in Simple Syllabus until we paste them in.");
    await expect(row).toContainText("Foundations of Web Development");
  } finally {
    await reviewerContext.close();
  }
});
