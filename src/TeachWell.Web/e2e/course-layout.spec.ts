import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

test("course page sections collapse, remember their state, and long lists are searchable", async ({ page, context }) => {
  await authenticate(context, "instructor");
  await page.goto("/");
  const openCourse = () =>
    page
      .getByRole("article")
      .filter({ has: page.getByRole("heading", { name: "Foundations of Web Development" }) })
      .getByRole("button", { name: "Explore course" })
      .click();
  await openCourse();

  const content = page.getByRole("region", { name: "Course content" });
  await expect(content).toContainText("1 module · 1 page · 1 assignment");
  // A small course opens its modules; each item is a heading inside its module.
  await expect(content.getByRole("heading", { name: "Welcome and learning goals" })).toBeVisible();

  // Formatted is the default view: the page's own heading and list render; Plain text shows the stored text instead.
  const welcome = content.locator(".item-row", { has: page.getByRole("heading", { name: "Welcome and learning goals" }) });
  await expect(welcome.locator(".item-preview h2", { hasText: "Welcome" })).toBeVisible();
  await expect(welcome.locator(".item-preview li", { hasText: "Write descriptive links" })).toBeVisible();
  await content.getByRole("button", { name: "Plain text" }).click();
  await expect(welcome.locator(".item-preview")).toHaveCount(0);
  await expect(welcome).toContainText("Write descriptive links");
  await content.getByRole("button", { name: "Formatted" }).click();
  await expect(welcome.locator(".item-preview")).toBeVisible();

  await content.getByRole("searchbox", { name: "Find a page or assignment" }).fill("first accessible");
  await expect(content.getByRole("heading", { name: "Your first accessible page" })).toBeVisible();
  await expect(content.getByRole("heading", { name: "Welcome and learning goals" })).toHaveCount(0);
  await content.getByRole("searchbox", { name: "Find a page or assignment" }).fill("");

  await content.getByRole("button", { name: "Collapse module Start here" }).click();
  await expect(content.getByRole("heading", { name: "Welcome and learning goals" })).toHaveCount(0);
  await content.getByRole("button", { name: "Expand all modules" }).click();
  await expect(content.getByRole("heading", { name: "Welcome and learning goals" })).toBeVisible();

  // Whole sections fold to a heading and a one-line summary, and stay folded after a reload.
  await page.getByRole("button", { name: "Collapse Course content" }).click();
  await expect(content.getByRole("searchbox")).toHaveCount(0);
  await expect(page.getByRole("heading", { name: "Course content" })).toBeVisible();
  await page.reload();
  await openCourse();
  await expect(page.getByRole("button", { name: "Expand Course content" })).toBeVisible();
  await page.getByRole("button", { name: "Expand Course content" }).click();

  // Unmapped assignments are a sorted, collapsible list rather than one long sentence.
  await page.getByRole("button", { name: "Expand Alignment and quality" }).click();
  const reports = page.getByRole("region", { name: "Alignment and quality" });
  await reports.getByText(/^Not yet mapped to a competency \(\d+ assessments?\)$/).click();
  await expect(reports.locator(".name-list li", { hasText: "Your first accessible page" })).toBeVisible();
});
