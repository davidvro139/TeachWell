import { expect, test } from "@playwright/test";
import { authenticate } from "./auth";

test("CTL manages a knowledge article, a workshop and a full ticket lifecycle; instructor sees results, self-serves and requests help from a task", async ({
  browser,
  page,
  context,
}) => {
  await authenticate(context, "reviewer");
  const ownerContext = await browser.newContext({ baseURL: "https://localhost:7443", ignoreHTTPSErrors: true });
  await authenticate(ownerContext, "instructor");
  const owner = await ownerContext.newPage();
  try {
    await page.goto("/");
    await page.getByRole("combobox", { name: "Workspace role" }).selectOption("CTLReviewer");
    await page.getByRole("button", { name: "Support", exact: true }).click();
    await expect(page.getByRole("heading", { name: "Support" })).toBeVisible();

    await page.getByRole("button", { name: "Knowledge base", exact: true }).click();
    await page.getByRole("button", { name: "New article", exact: true }).click();
    await page.getByLabel("Title", { exact: true }).fill("Choosing a workshop modality");
    await page.getByLabel("Body", { exact: true }).fill("Online works well for short sessions; in-person suits hands-on practice.");
    await page.getByRole("button", { name: "Create draft", exact: true }).click();
    await expect(page.getByText("DRAFT · V1")).toBeVisible();
    await page.getByRole("combobox", { name: "Status", exact: true }).selectOption("Published");
    await page.getByRole("button", { name: "Save", exact: true }).click();
    await expect(page.getByText("Article published.")).toBeVisible();

    await page.getByRole("button", { name: "Workshops", exact: true }).click();
    await page.getByRole("button", { name: "New workshop", exact: true }).click();
    await page.getByLabel("Title", { exact: true }).fill("Modality choices for training");
    const start = new Date(Date.now() + 14 * 86400000);
    const toLocalInput = (d: Date) => new Date(d.getTime() - d.getTimezoneOffset() * 60000).toISOString().slice(0, 16);
    await page.getByLabel("Starts", { exact: true }).fill(toLocalInput(start));
    await page.getByLabel("Ends", { exact: true }).fill(toLocalInput(new Date(start.getTime() + 3600000)));
    await page.getByLabel("Capacity (optional)").fill("2");
    await page.getByRole("button", { name: "Create workshop", exact: true }).click();
    await expect(page.getByText("Modality choices for training")).toBeVisible();

    // The instructor files a standalone ticket (no task link) before the CTL side does anything with it.
    await owner.goto("/");
    await owner.getByRole("button", { name: "Support", exact: true }).click();
    await expect(owner.getByRole("button", { name: "Triage queue" })).toHaveCount(0); // no scope toggle for a non-CTL user
    await owner.getByRole("button", { name: "New support ticket", exact: true }).click();
    await owner.getByRole("combobox", { name: "Category", exact: true }).selectOption("CourseDesign");
    await owner.getByLabel("Title", { exact: true }).fill("Should I run this as one workshop or two?");
    await owner.getByLabel("What do you need help with?").fill("Trying to decide between one combined session and two shorter ones.");
    await owner.getByRole("button", { name: "Submit ticket", exact: true }).click();
    await expect(owner.getByRole("heading", { level: 1, name: "Should I run this as one workshop or two?" })).toBeVisible();

    // The CTL finds it via the tenant-wide triage queue, not "My tickets" — Jordan Lee neither filed nor is assigned it yet.
    await page.getByRole("button", { name: "Tickets", exact: true }).click();
    await page.getByRole("button", { name: "Triage queue", exact: true }).click();
    await page.getByRole("button", { name: "Open ticket: Should I run this as one workshop or two?" }).click();
    await expect(page.getByRole("heading", { level: 1, name: "Should I run this as one workshop or two?" })).toBeVisible();

    await page.getByRole("button", { name: "Mark triaged" }).click();
    await expect(page.getByText("Mark triaged applied.")).toBeVisible();
    await page.getByRole("button", { name: "Start working" }).click();

    await page.getByRole("combobox", { name: "Knowledge article" }).selectOption({ label: "Choosing a workshop modality" });
    await page.getByRole("combobox", { name: "Workshop" }).selectOption({ label: "Modality choices for training" });
    await page.getByRole("button", { name: "Save recommendation", exact: true }).click();
    await expect(page.getByText("Recommendation updated.")).toBeVisible();

    await page.getByRole("button", { name: "Resolve ticket" }).click();
    await page.getByLabel('Note for "Resolve ticket" (required)').fill("Recommended the job aid and the upcoming workshop.");
    await page.getByRole("button", { name: "Confirm Resolve ticket" }).click();
    await page.getByRole("button", { name: "Close ticket" }).click();
    await expect(page.locator("span.tag.status-closed")).toBeVisible();

    // The instructor's own queue now shows it, with the recommendations visible but not editable.
    await owner.getByRole("button", { name: "Support", exact: true }).click();
    await owner.getByRole("button", { name: "Open ticket: Should I run this as one workshop or two?" }).click();
    await expect(owner.getByText("Article: Choosing a workshop modality")).toBeVisible();
    await expect(owner.getByText("Workshop: Modality choices for training")).toBeVisible();
    await expect(owner.getByRole("combobox", { name: "Knowledge article" })).toHaveCount(0);

    await owner.getByRole("button", { name: "← Back", exact: true }).click();
    await owner.getByRole("button", { name: "Knowledge base", exact: true }).click();
    await owner.getByRole("button", { name: "Choosing a workshop modality" }).click();
    await expect(owner.getByText("Online works well for short sessions", { exact: false })).toBeVisible();
    await expect(owner.getByRole("button", { name: "Save", exact: true })).toHaveCount(0);

    await owner.getByRole("button", { name: "Workshops", exact: true }).click();
    const workshopRow = owner.getByRole("listitem").filter({ hasText: "Modality choices for training" });
    await expect(workshopRow).toContainText("0/2 registered");
    await workshopRow.getByRole("button", { name: "Register" }).click();
    await expect(owner.getByText("You are registered.")).toBeVisible();
    await expect(workshopRow).toContainText("You are registered");

    // Back on the CTL side: a revision task's "Request help" links a new ticket straight back to it.
    await page.getByRole("button", { name: "Courses", exact: true }).click();
    await page
      .getByRole("article")
      .filter({ has: page.getByRole("heading", { name: "Foundations of Web Development" }) })
      .getByRole("button", { name: "Explore course" })
      .click();
    await page.getByRole("combobox", { name: "Element" }).selectOption({ label: "Assignment · Your first accessible page" });
    await page.getByRole("button", { name: "Start review" }).click();
    await page.getByRole("button", { name: "Run automated checks" }).click();
    await page.getByRole("button", { name: "Confirm finding: Image has no text alternative" }).click();
    for (const title of ["Table has no header cells", "Assignment has no attached rubric"])
      await page.getByRole("button", { name: `Reject finding: ${title}` }).click();
    await page
      .getByRole("group", { name: "Rating for ASM-1" })
      .getByRole("radio", { name: /^1 · Partially demonstrated/ })
      .check();
    await page.getByRole("button", { name: "Save ASM-1" }).click();
    await page.getByRole("button", { name: "Publish review" }).click();

    await page.getByRole("button", { name: "Create a revision task from confirmed findings" }).click();
    await page.getByLabel("Image has no text alternative").check();
    await page.getByLabel("Title", { exact: true }).fill("Add alt text");
    await page.getByRole("button", { name: "Create task", exact: true }).click();
    await page.getByRole("button", { name: "Assign to instructor" }).click();

    await page.getByRole("button", { name: "Request help with this task" }).click();
    await expect(page.getByRole("heading", { name: "Request help with this revision task" })).toBeVisible();
    await page.getByRole("combobox", { name: "Category", exact: true }).selectOption("Accessibility");
    await page.getByLabel("Title", { exact: true }).fill("Not sure how to phrase this alt text");
    await page.getByLabel("What do you need help with?").fill("The layout image needs alt text but I'm not sure what to say.");
    await page.getByRole("button", { name: "Submit ticket", exact: true }).click();

    await expect(page.getByRole("heading", { level: 1, name: "Not sure how to phrase this alt text" })).toBeVisible();
    await expect(page.getByText("Your first accessible page", { exact: false })).toBeVisible();
    await expect(page.getByText("Revision task: Add alt text", { exact: false })).toBeVisible();
  } finally {
    await ownerContext.close();
  }
});
