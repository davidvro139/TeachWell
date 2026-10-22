import { expect, test, type Page } from "@playwright/test";
import { authenticate } from "./auth";

// Uses WEB 101, already shared with the demo reviewer: taking a request on DES 120 would share it and break sharing.spec, which
// relies on DES 120 starting unshared. Endorsement (which needs a published review) is covered by ReadinessTests.
async function openCourse(page: Page, title: string) {
  await page
    .getByRole("article")
    .filter({ has: page.getByRole("heading", { name: title }) })
    .getByRole("button", { name: "Explore course" })
    .click();
}

test("instructor requests a readiness review, the CTL takes it and requests changes, the instructor sees why and asks again", async ({
  browser,
  page,
  context,
}) => {
  await authenticate(context, "instructor");
  await page.goto("/");
  await openCourse(page, "Foundations of Web Development");
  const readiness = page.getByRole("heading", { name: "CTL readiness review" }).locator("xpath=ancestor::section[1]");
  await readiness.getByLabel("Note for the CTL (optional)").fill("Teaching this online for the first time in spring.");
  await readiness.getByRole("button", { name: "Request readiness review" }).click();
  await expect(readiness.getByText("Readiness review requested.")).toBeVisible();
  await expect(readiness.locator(".readiness-active")).toContainText("Waiting for a CTL reviewer");

  const reviewerContext = await browser.newContext({ baseURL: "https://localhost:7443", ignoreHTTPSErrors: true });
  try {
    await authenticate(reviewerContext, "reviewer");
    const ctl = await reviewerContext.newPage();
    await ctl.goto("/");
    // The reviewer hears about the request from the notification bell, which leads to the queue.
    await ctl.getByRole("button", { name: /^Notifications, \d+ unread$/ }).click();
    await ctl.getByRole("region", { name: "Notifications" }).getByRole("button", { name: /Readiness review requested: WEB 101/ }).click();
    await expect(ctl.getByRole("heading", { level: 1, name: "Readiness reviews" })).toBeVisible();
    await expect(ctl.getByText('"Teaching this online for the first time in spring."')).toBeVisible();
    await ctl.getByRole("button", { name: "Take request for WEB 101" }).click();

    // Taking the request opens the course, where the decision is made next to the course's reviews.
    const decide = ctl.getByRole("heading", { name: "Your decision" }).locator("xpath=..");
    await expect(decide).toBeVisible();
    await decide.getByRole("radio", { name: "Endorse" }).check();
    // The standards the request was made under say what the endorsement needs; nothing is met yet.
    await expect(decide.getByText("You have published a review of this course", { exact: false })).toBeVisible();
    await expect(decide.locator(".is-unmet")).toHaveCount(1);
    await expect(decide.getByRole("button", { name: "Endorse course" })).toBeDisabled();
    await decide.getByRole("radio", { name: "Request changes" }).check();
    await decide.getByLabel("What needs to change").fill("Define the course competencies and map the assignment to them.");
    await decide.getByRole("button", { name: "Request changes" }).click();
    await expect(ctl.getByText("Changes requested.", { exact: true })).toBeVisible();
  } finally {
    await reviewerContext.close();
  }

  await page.reload();
  // The course card carries the outcome, and the bell says why.
  await expect(
    page.getByRole("article").filter({ has: page.getByRole("heading", { name: "Foundations of Web Development" }) }).getByText("CHANGES REQUESTED"),
  ).toBeVisible();
  await page.getByRole("button", { name: /^Notifications, \d+ unread$/ }).click();
  await expect(page.getByRole("region", { name: "Notifications" }).getByText("Changes requested for WEB 101")).toBeVisible();
  await page.getByRole("button", { name: "Mark all read" }).click();
  await expect(page.getByRole("button", { name: "Notifications", exact: true })).toBeVisible();
  await page.keyboard.press("Escape");
  await openCourse(page, "Foundations of Web Development");
  await expect(readiness.getByText("Define the course competencies and map the assignment to them.").first()).toBeVisible();
  await readiness.getByRole("button", { name: "Request readiness review" }).click();
  await expect(readiness.locator(".readiness-active")).toContainText("Waiting for a CTL reviewer");
  // Leave WEB 101 as later specs expect it: no open request.
  await readiness.getByRole("button", { name: "Withdraw request" }).click();
  await expect(readiness.getByText("Request withdrawn.")).toBeVisible();
});
