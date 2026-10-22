import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

test("CTL reviewer runs checks, dispositions findings, publishes; owner sees only confirmed results", async ({
  browser,
  page,
  context,
}) => {
  await authenticate(context, "reviewer");
  await page.goto("/");
  await page
    .getByRole("combobox", { name: "Workspace role" })
    .selectOption("CTLReviewer");

  await page.getByRole("button", { name: "Rubrics", exact: true }).click();
  await expect(
    page.getByRole("heading", { name: "Rubric library" }),
  ).toBeVisible();
  await page
    .getByRole("button", { name: "Version 1 · Published · 10 criteria" })
    .click();
  await expect(
    page.getByText("This version can no longer change.", { exact: false }),
  ).toBeVisible();
  await expect(
    page.getByRole("button", { name: "Save draft" }),
  ).toHaveCount(0);

  await page.getByRole("button", { name: "Courses", exact: true }).click();
  await page
    .getByRole("article")
    .filter({
      has: page.getByRole("heading", {
        name: "Foundations of Web Development",
      }),
    })
    .getByRole("button", { name: "Explore course" })
    .click();
  await expect(
    page.getByRole("heading", { name: "Reviews", exact: true }),
  ).toBeVisible();
  await page
    .getByRole("combobox", { name: "Element" })
    .selectOption({ label: "Assignment · Your first accessible page" });
  await page.getByRole("button", { name: "Start review" }).click();

  await expect(
    page.getByRole("heading", { level: 1, name: "Your first accessible page" }),
  ).toBeVisible();
  await expect(page.getByText("DRAFT (VISIBLE ONLY TO YOU)", { exact: false })).toBeVisible();
  await expect(page.getByRole("button", { name: "Publish review" })).toBeDisabled();
  await page.getByRole("button", { name: "Run automated checks" }).click();
  await expect(
    page.getByText("Automated checks ran against the pinned snapshot."),
  ).toBeVisible();
  for (const title of ["Image has no text alternative", "Assignment has no attached rubric"]) {
    await page.getByRole("button", { name: `Confirm finding: ${title}` }).click();
    await expect(page.getByText("Finding confirmed.")).toBeVisible();
  }
  await page
    .getByRole("button", { name: "Reject finding: Table has no header cells" })
    .click();
  await expect(page.getByText("Finding rejected.")).toBeVisible();

  const rating = page.getByRole("group", { name: "Rating for ASM-1" });
  await rating.getByRole("radio", { name: /^1 · Partially demonstrated/ }).check();
  await page.getByRole("button", { name: "Save ASM-1" }).click();
  await expect(page.getByText("ASM-1 saved.")).toBeVisible();
  // One of ten criteria rated 1/3: coverage 10%, score over rated criteria only.
  const summary = page.getByRole("region", { name: "Rubric summary" });
  await expect(summary.getByText("10%", { exact: true })).toBeVisible();
  await expect(summary.getByText("33.3%", { exact: true })).toBeVisible();
  await page
    .getByLabel("Summary for the instructor (optional)")
    .fill("Please state how the page and reflection are assessed.");
  await page.getByRole("button", { name: "Publish review" }).click();
  await expect(
    page.getByText("Review published.", { exact: false }),
  ).toBeVisible();
  await expect(page.getByRole("radio")).toHaveCount(0);

  const ownerContext = await browser.newContext({
    baseURL: "https://localhost:7443",
    ignoreHTTPSErrors: true,
  });
  try {
    await authenticate(ownerContext, "instructor");
    const owner = await ownerContext.newPage();
    await owner.goto("/");
    await owner
      .getByRole("article")
      .filter({
        has: owner.getByRole("heading", {
          name: "Foundations of Web Development",
        }),
      })
      .getByRole("button", { name: "Explore course" })
      .click();
    await expect(
      owner.getByRole("heading", { name: "Published reviews" }),
    ).toBeVisible();
    await owner
      .getByRole("button", { name: "Open review of Your first accessible page" })
      .click();
    await expect(owner.getByText("Assignment has no attached rubric")).toBeVisible();
    await expect(owner.getByText("Image has no text alternative")).toBeVisible();
    await expect(owner.getByText("Table has no header cells")).toHaveCount(0);
    await expect(
      owner.getByText("Please state how the page and reflection are assessed."),
    ).toBeVisible();
    await expect(owner.getByRole("button", { name: "Publish review" })).toHaveCount(0);
    await expect(owner.getByRole("button", { name: /Run automated checks/ })).toHaveCount(0);
  } finally {
    await ownerContext.close();
  }
});
