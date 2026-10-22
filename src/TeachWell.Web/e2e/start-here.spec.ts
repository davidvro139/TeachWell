import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

const WEB101 = "30000000-0000-0000-0000-000000000001";

test("an instructor follows the Start here checklist to bring in a course, then into the guide; the course page has a header and section links", async ({
  page,
  context,
}) => {
  await authenticate(context, "instructor");
  // Earlier journeys change this instructor's real progress, so the checklist is given a fixed starting point here;
  // GettingStartedTests covers how the server works progress out.
  let progress = { broughtInACourse: false, markedAStep: false, askedForReadiness: false, endorsed: false, courseId: WEB101, courseTitle: "Foundations of Web Development" };
  await page.route("**/api/v1/getting-started", (route) => route.fulfill({ json: progress }));
  await page.goto("/");

  const checklist = page.getByRole("region", { name: "Your path to a stronger course" });
  await expect(checklist).toContainText("0 of 4 done.");
  await expect(checklist.getByRole("listitem")).toHaveCount(4);
  const current = checklist.locator('[aria-current="step"]');
  await expect(current).toContainText("Bring in your course");
  await current.getByRole("button", { name: "Bring in a course" }).click();
  // Step-by-step export help for someone who has never done it.
  await expect(page.getByText("Export Course Content", { exact: true })).toBeVisible();
  await expect(page.getByText("Stuck at any step? Ask the CTL from Support", { exact: false })).toBeVisible();

  // Next step: the guide for their course, opened at the guide.
  progress = { ...progress, broughtInACourse: true };
  await page.getByRole("navigation", { name: "Workspace sections" }).getByRole("button", { name: "Courses", exact: true }).click();
  await expect(checklist).toContainText("1 of 4 done.");
  await expect(checklist.locator('[aria-current="step"]')).toContainText("Work through the improvement guide");
  await checklist.getByRole("button", { name: "Open the guide for Foundations of Web Development" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Foundations of Web Development" })).toBeVisible();
  await expect(page.getByRole("heading", { level: 2, name: "Improvement guide" })).toBeInViewport();
  await expect(page.getByText("Nothing you do in TeachWell changes your Canvas course.", { exact: false })).toBeVisible();

  // "On this page" opens a folded section and goes to it.
  const sections = page.getByRole("navigation", { name: "On this page" });
  const standardsToggle = page.getByRole("button", { name: /Expand Course standards|Collapse Course standards/ });
  await expect(standardsToggle).toHaveAttribute("aria-expanded", "false");
  await sections.getByRole("button", { name: "Course standards" }).click();
  await expect(standardsToggle).toHaveAttribute("aria-expanded", "true");
  await expect(page.getByRole("heading", { name: "Course standards" })).toBeInViewport();

  // The checklist can be put away and brought back; the choice is remembered.
  await page.getByRole("button", { name: "← Back to courses" }).click();
  await checklist.getByRole("button", { name: "Hide this checklist" }).click();
  await expect(checklist).toHaveCount(0);
  await page.reload();
  await page.getByRole("button", { name: "Show the Start here checklist" }).click();
  await expect(checklist).toBeVisible();

  // How the tools relate, in one place, with explanations.
  const how = page.getByRole("region", { name: /Four tools, one goal/ });
  await expect(how.getByRole("listitem")).toHaveCount(4);
  await how.getByRole("button", { name: "Readiness review" }).click();
  await expect(page.getByRole("dialog", { name: "Readiness review" })).toContainText("endorses the course or asks for specific changes");
});
