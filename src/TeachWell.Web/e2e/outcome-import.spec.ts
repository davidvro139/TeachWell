import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

// Uses DES 120 (the reports spec relies on WEB 101 starting without competencies).
test("instructor pastes Simple Syllabus outcomes, edits and approves them as competencies", async ({ page, context }) => {
  await authenticate(context, "instructor");
  await page.goto("/");
  await page
    .getByRole("article")
    .filter({ has: page.getByRole("heading", { name: "Designing for Everyone" }) })
    .getByRole("button", { name: "Explore course" })
    .click();
  await page.getByRole("button", { name: "Expand Alignment and quality" }).click();
  await page.getByRole("button", { name: "Paste outcomes" }).click();

  const tool = page.getByRole("region", { name: "Paste learning outcomes" });
  await tool.getByLabel("Outcomes").fill(
    [
      "Student Learning Outcomes",
      "Implement common HTML tags in a functional coding format to create a Web site using current standards and technologies.",
      "Demonstrate the ability to upload and publish a web page on a web server.",
      "Investigate web scripting and the HTML DOM / Manipulate the DOM using web scripts.",
      "Use web hosting service to deploy a website.",
      "Explore best practices in modern responsive website design.",
    ].join("\n"),
  );
  await tool.getByRole("button", { name: "Read outcomes" }).click();

  await expect(tool.getByText("TeachWell found 5 outcomes", { exact: false })).toBeVisible();
  await expect(tool.getByText("Ignored as headings: Student Learning Outcomes")).toBeVisible();
  for (let i = 1; i <= 5; i++) await expect(tool.getByLabel(`Code for outcome ${i}`)).toHaveValue(`SLO-${i}`);
  await expect(tool.getByText("Bloom: Apply").first()).toBeVisible();

  // Edit one, rename a code, skip the last, then approve.
  await tool.getByLabel("Outcome 4", { exact: true }).fill("Use a web hosting service to deploy a website.");
  await tool.getByLabel("Code for outcome 2").fill("SLO-PUBLISH");
  await tool.locator("li").nth(4).getByRole("checkbox", { name: "Add" }).uncheck();
  await tool.getByRole("button", { name: "Add 4 competencies" }).click();

  await expect(page.getByText("Added 4 competencies.")).toBeVisible();
  const list = page.locator(".competency-list");
  await expect(list).toContainText("Use a web hosting service to deploy a website.");
  await expect(list).toContainText("SLO-PUBLISH");
  await expect(list).not.toContainText("Explore best practices");
});
