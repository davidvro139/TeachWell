import { expect, test } from "@playwright/test";
import { randomUUID } from "node:crypto";
import { authenticate } from "./auth";

test("course listing/import failure, success and safe imported-content browsing (mock HTTP)", async ({
  page,
  context,
}) => {
  await authenticate(context, "instructor");
  const connectionId = randomUUID(),
    courseId = randomUUID(),
    moduleId = randomUUID(),
    pageId = randomUUID();
  let imported = false,
    reject = true;
  // Explicit UI fixture. The server's import transaction and Canvas adapter are tested separately.
  await page.route("**/api/v1/canvas/**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/settings"))
      return route.fulfill({
        json: {
          enabled: true,
          allowedOrigins: ["https://canvas.example.test/"],
        },
      });
    if (path.endsWith("/courses"))
      return route.fulfill({
        json: [
          { id: "42", title: "Fictional imported course", code: "TEST 42" },
        ],
      });
    return route.fulfill({
      json: [
        {
          id: connectionId,
          baseUrl: "https://canvas.example.test/",
          status: "Connected",
          canvasUserId: "7654321",
          tokenMask: "Saved token",
          expiresAt: null,
          lastValidatedAt: new Date().toISOString(),
          lastCheckedAt: null,
          errorCode: null,
        },
      ],
    });
  });
  await page.route("**/api/v1/courses**", async (route) => {
    const path = new URL(route.request().url()).pathname;
    if (path.endsWith("/import")) {
      expect(route.request().postDataJSON()).toEqual({
        connectionId,
        sourceCourseId: "42",
      });
      if (reject) {
        reject = false;
        return route.fulfill({
          status: 503,
          json: { title: "Canvas temporarily unavailable." },
        });
      }
      imported = true;
      return route.fulfill({
        json: {
          courseId,
          elementCount: 5,
          warnings: ["A quiz was not imported."],
        },
      });
    }
    if (
      path === `/api/v1/courses/${courseId}/reviewers` ||
      path === `/api/v1/courses/${courseId}/shares` ||
      path === `/api/v1/courses/${courseId}/reviews` ||
      path === `/api/v1/courses/${courseId}/revision-tasks` ||
      path === `/api/v1/courses/${courseId}/competencies`
    )
      return route.fulfill({ json: [] });
    if (path === `/api/v1/courses/${courseId}/standards`)
      return route.fulfill({
        json: {
          courseId,
          deliveryMode: null,
          department: null,
          canClassify: false,
          applied: { setId: null, versionId: null, name: "TeachWell built-in defaults", versionNumber: null, scope: "College-wide", reason: "Fixture.", publishedAt: null },
        },
      });
    if (path === `/api/v1/courses/${courseId}/readiness`)
      return route.fulfill({
        json: {
          courseId,
          isOwner: true,
          canRequest: true,
          blockers: [],
          requiredOpenSteps: 0,
          active: null,
          lastDecision: null,
          endorsement: null,
          history: [],
          canDecide: false,
          hasQualifyingReview: false,
        },
      });
    // The course page asks for the syllabus (ADR 019). Without this the catch-all below answers with the course
    // list, which is not a syllabus at all; the panel is defensive about that, but the fixture should still model
    // what the API really returns.
    if (path === `/api/v1/courses/${courseId}/syllabus`)
      return route.fulfill({
        json: {
          courseId,
          source: "None",
          words: 0,
          text: null,
          suppliedAt: null,
          suppliedByName: null,
          importedAt: null,
          canEdit: true,
          note: "Fixture: this course has no syllabus.",
        },
      });
    // The course page also asks for the conformance report (ADR 018). The panel is defensive about a wrong body,
    // but the fixture should model what the API really returns.
    if (path === `/api/v1/courses/${courseId}/conformance`)
      return route.fulfill({
        json: {
          courseId,
          courseTitle: "Fictional imported course",
          courseCode: "TEST 42",
          standard: "WCAG 2.1",
          asOf: new Date().toISOString(),
          contentAsOf: null,
          standards: { name: "TeachWell built-in defaults", versionNumber: null },
          checkedItems: 0,
          criteriaInScope: 0,
          criteriaOutOfScope: 50,
          criteriaWithFindings: 0,
          criteriaWithNoFindings: 0,
          criteriaNotChecked: 0,
          openPlaces: 0,
          donePlaces: 0,
          notApplicablePlaces: 0,
          criteria: [],
          endorsement: null,
          endorsedAt: null,
          caveat: "Fixture caveat.",
          limitations: [],
        },
      });
    if (path === `/api/v1/courses/${courseId}/guide`)
      return route.fulfill({
        json: {
          courseId,
          courseTitle: "Fictional imported course",
          standards: { setId: null, versionId: null, name: "TeachWell built-in defaults", versionNumber: null, scope: "College-wide", reason: "Fixture.", publishedAt: null },
          contentAsOf: null,
          canMark: true,
          checkedItems: 0,
          aspects: [],
          steps: [],
          strengths: [],
          resolvedSinceMarked: 0,
          limitations: [],
          reviewerNotes: [],
        },
      });
    if (path === `/api/v1/courses/${courseId}`)
      return route.fulfill({
        json: {
          id: courseId,
          title: "Fictional imported course",
          code: "TEST 42",
          sourceType: "Canvas",
          sourceOrigin: "https://canvas.example.test/",
          sourceCourseId: "42",
          lastSyncedAt: new Date().toISOString(),
          warnings: ["A quiz was not imported."],
          permissions: { canManageSharing: true },
          elements: [
            {
              id: moduleId,
              type: "module",
              title: "Start",
              plainText: "",
              sourceStatus: "Published",
              parentElementId: null,
            },
            {
              id: pageId,
              type: "page",
              title: "Orientation",
              plainText:
                "Read and reflect. <img src=x onerror='window.untrustedExecuted=true'>",
              parentElementId: null,
              sourceStatus: "Published",
              sourceUrl:
                "https://canvas.example.test/courses/42/pages/orientation",
            },
            {
              id: randomUUID(),
              type: "moduleItem",
              title: "Read first",
              plainText: "",
              parentElementId: moduleId,
              targetElementId: pageId,
              sourceStatus: "Published",
            },
            {
              id: randomUUID(),
              type: "moduleItem",
              title: "Read again",
              plainText: "",
              parentElementId: moduleId,
              targetElementId: pageId,
              sourceStatus: "Published",
            },
            {
              id: randomUUID(),
              type: "assignment",
              title: "Practice",
              plainText: "Explain a design choice.",
              parentElementId: null,
              sourceStatus: "Unpublished",
              sourceUrl: "javascript:alert('blocked')",
            },
          ],
        },
      });
    return route.fulfill({
      json: imported
        ? [
            {
              id: courseId,
              title: "Fictional imported course",
              code: "TEST 42",
              sourceType: "Canvas",
              elementCount: 5,
              permission: "Manage",
            },
          ]
        : [],
    });
  });
  await page.goto("/");
  await page
    .getByRole("button", { name: "Import courses", exact: true })
    .click();
  await page.getByRole("button", { name: "List available courses" }).click();
  const importButton = page.getByRole("button", {
    name: "Import Fictional imported course",
    exact: true,
  });
  await importButton.click();
  await expect(page.getByRole("alert")).toContainText(
    "previous local course copy is preserved",
  );
  await importButton.click();
  await expect(page.getByRole("status")).toContainText("5 elements imported");
  await page.getByRole("button", { name: "Courses", exact: true }).click();
  await page
    .getByRole("article")
    .filter({
      has: page.getByRole("heading", { name: "Fictional imported course" }),
    })
    .getByRole("button", { name: "Explore course" })
    .click();
  await expect(
    page.getByText(/· FROM CANVAS$/),
  ).toBeVisible();
  await expect(page.getByRole("heading", { name: "Read first" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Read again" })).toBeVisible();
  await expect(
    page.getByRole("heading", { name: "Other course content" }),
  ).toBeVisible();
  // Content outside any module starts folded.
  await page.getByRole("button", { name: "Expand other course content" }).click();
  await expect(
    page.getByRole("heading", { name: "Import limitations" }),
  ).toBeVisible();
  await expect(page.locator(".element img")).toHaveCount(0);
  await expect(
    page.getByRole("link", { name: "Open source in Canvas", exact: false }),
  ).toHaveCount(2);
  expect(await page.evaluate(() => "untrustedExecuted" in window)).toBe(false);
});
