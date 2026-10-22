import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

test("CTL reviewer creates a revision task from a published review; instructor works it through a demo simulated update to resolution", async ({
  browser,
  page,
  context,
}) => {
  await authenticate(context, "reviewer");
  await page.goto("/");
  await page.getByRole("combobox", { name: "Workspace role" }).selectOption("CTLReviewer");

  await page
    .getByRole("article")
    .filter({ has: page.getByRole("heading", { name: "Foundations of Web Development" }) })
    .getByRole("button", { name: "Explore course" })
    .click();
  await page.getByRole("combobox", { name: "Element" }).selectOption({ label: "Assignment · Your first accessible page" });
  await page.getByRole("button", { name: "Start review" }).click();
  await expect(page.getByRole("heading", { level: 1, name: "Your first accessible page" })).toBeVisible();
  await page.getByRole("button", { name: "Run automated checks" }).click();
  await page.getByRole("button", { name: "Confirm finding: Image has no text alternative" }).click();
  for (const title of ["Table has no header cells", "Assignment has no attached rubric"])
    await page.getByRole("button", { name: `Reject finding: ${title}` }).click();
  const rating = page.getByRole("group", { name: "Rating for ASM-1" });
  await rating.getByRole("radio", { name: /^1 · Partially demonstrated/ }).check();
  await page.getByRole("button", { name: "Save ASM-1" }).click();
  await page.getByRole("button", { name: "Publish review" }).click();
  await expect(page.getByText("Review published.", { exact: false })).toBeVisible();

  await page.getByRole("button", { name: "Create a revision task from confirmed findings" }).click();
  await page.getByLabel("Image has no text alternative").check();
  await page.getByLabel("Title", { exact: true }).fill("Add alt text to the layout image");
  await page.getByLabel("Instructions for the instructor").fill("Please add descriptive alt text to the sample layout image.");
  await page.getByRole("button", { name: "Create task", exact: true }).click();

  await expect(page.getByRole("heading", { level: 1, name: "Add alt text to the layout image" })).toBeVisible();
  await expect(page.locator("span.tag.status-draft")).toBeVisible();
  await page.getByRole("button", { name: "Assign to instructor" }).click();
  await expect(page.getByText("Assign to instructor applied.")).toBeVisible();

  const ownerContext = await browser.newContext({ baseURL: "https://localhost:7443", ignoreHTTPSErrors: true });
  try {
    await authenticate(ownerContext, "instructor");
    const owner = await ownerContext.newPage();
    await owner.goto("/");
    await owner
      .getByRole("article")
      .filter({ has: owner.getByRole("heading", { name: "Foundations of Web Development" }) })
      .getByRole("button", { name: "Explore course" })
      .click();
    await expect(owner.getByRole("heading", { name: "Revision tasks" })).toBeVisible();
    await owner.getByRole("button", { name: "Open task: Add alt text to the layout image" }).click();
    await expect(owner.getByRole("heading", { level: 1, name: "Add alt text to the layout image" })).toBeVisible();
    await expect(owner.getByText("Please add descriptive alt text", { exact: false })).toBeVisible();

    await owner.getByRole("button", { name: "Start work" }).click();
    await owner.getByLabel("Add a comment").fill("On it — fixing the image now.");
    await owner.getByRole("button", { name: "Post comment", exact: true }).click();
    await expect(owner.getByText("On it — fixing the image now.")).toBeVisible();

    await expect(owner.getByRole("heading", { name: "Demo: record an update made outside TeachWell" })).toBeVisible();
    const updatedText = owner.getByLabel("Updated text");
    await updatedText.fill((await updatedText.inputValue()) + " Added descriptive alt text to the layout image.");
    await owner.getByRole("button", { name: "Record simulated update" }).click();
    await expect(owner.getByText("Demo content updated.", { exact: false })).toBeVisible();

    await owner.getByRole("button", { name: "Submit for review" }).click();
    // Wait for the server to accept the submission before the reviewer looks.
    await expect(owner.locator("span.tag.status-readyforreview")).toBeVisible();
    await expect(owner.getByRole("button", { name: "Resolve task" })).toHaveCount(0);

    // Reviewer sees the comparison reflect the simulated edit, and resolves the task.
    await page.goto("https://localhost:7443/");
    await page.getByRole("combobox", { name: "Workspace role" }).selectOption("CTLReviewer");
    await page.getByRole("button", { name: "Revision tasks", exact: true }).click();
    await page.getByRole("button", { name: "Open task: Add alt text to the layout image" }).click();
    await expect(page.locator("span.tag.status-readyforreview")).toBeVisible();
    await expect(page.locator(".text-diff")).toContainText("Added descriptive alt text to the layout image.");
    await page.getByRole("button", { name: "Resolve task" }).click();
    await expect(page.getByText("Resolve task applied.")).toBeVisible();
    await expect(page.getByRole("heading", { name: "Actions" })).toHaveCount(0);
  } finally {
    await ownerContext.close();
  }
});
