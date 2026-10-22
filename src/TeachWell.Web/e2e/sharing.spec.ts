import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

test("instructor shares a private course, reviewer opens it, owner revokes access", async ({
  browser,
  page,
  context,
}) => {
  await page.goto("/");
  await expect(
    page.getByRole("heading", { name: "Welcome to your workspace" }),
  ).toBeVisible();
  await expect(page.getByLabel("Email", { exact: true })).toBeVisible();
  await expect(page.getByLabel("Password", { exact: true })).toHaveAttribute(
    "type",
    "password",
  );
  await authenticate(context, "instructor");
  await page.reload();
  await expect(
    page.getByRole("heading", { name: "Welcome back, Alex." }),
  ).toBeVisible();
  await page
    .getByRole("article")
    .filter({
      has: page.getByRole("heading", { name: "Designing for Everyone" }),
    })
    .getByRole("button", { name: "Explore course" })
    .click();
  await expect(
    page.getByText("Private to you. No CTL reviewers have access yet."),
  ).toBeVisible();
  const select = page.getByRole("combobox", { name: "Reviewer", exact: true });
  await select.focus();
  await expect(select).toBeFocused();
  await select.selectOption({ label: "Jordan Lee" });
  await page
    .getByRole("button", { name: "Share for review", exact: true })
    .click();
  await expect(
    page.getByText("Course shared for review.", { exact: false }),
  ).toBeVisible();
  const reviewerContext = await browser.newContext({
    baseURL: "https://localhost:7443",
    ignoreHTTPSErrors: true,
  });
  try {
    await authenticate(reviewerContext, "reviewer");
    const reviewer = await reviewerContext.newPage();
    await reviewer.goto("/");
    await reviewer
      .getByRole("combobox", { name: "Workspace role" })
      .selectOption("CTLReviewer");
    await expect(
      reviewer.getByRole("heading", { name: "Designing for Everyone" }),
    ).toBeVisible();
    await reviewer
      .getByRole("article")
      .filter({
        has: reviewer.getByRole("heading", { name: "Designing for Everyone" }),
      })
      .getByRole("button", { name: "Explore course" })
      .click();
    await expect(
      reviewer.getByRole("heading", { name: "Your first accessible page" }),
    ).toBeVisible();
    await expect(
      reviewer.getByRole("heading", { name: "Share with a CTL reviewer" }),
    ).toHaveCount(0);
    await page
      .getByRole("button", { name: "Remove Jordan Lee's course access" })
      .click();
    await expect(
      page.getByText("Reviewer access removed.", { exact: false }),
    ).toBeVisible();
    expect(
      (
        await reviewerContext.request.get(
          "/api/v1/courses/30000000-0000-0000-0000-000000000002",
        )
      ).status(),
    ).toBe(404);
    await reviewer
      .getByRole("button", { name: "Back to courses", exact: false })
      .click();
    await reviewer
      .getByRole("button", { name: "Courses", exact: true })
      .click();
    await expect(
      reviewer.getByRole("heading", { name: "Designing for Everyone" }),
    ).toHaveCount(0);
    await expect(
      reviewer.getByRole("heading", { name: "Foundations of Web Development" }),
    ).toBeVisible();
    const cookies = await context.cookies();
    const session = cookies.find((c) => c.name === "__Host-TeachWell");
    expect(session?.secure).toBe(true);
    expect(session?.httpOnly).toBe(true);
    expect(session?.sameSite).toBe("Strict");
  } finally {
    await reviewerContext.close();
  }
});
