using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TeachWell.Application;
using TeachWell.Domain;
using TeachWell.Infrastructure.Canvas;
using TeachWell.Infrastructure.Reports;
using TeachWell.Infrastructure.Reviews;
using TeachWell.Infrastructure.Support;
using TeachWell.Infrastructure.Tasks;
namespace TeachWell.Infrastructure;

public static class DemoSeed
{
    public static readonly Guid Tenant = Guid.Parse("10000000-0000-0000-0000-000000000001");
    private static readonly Guid Course1 = Guid.Parse("30000000-0000-0000-0000-000000000001"), Course2 = Guid.Parse("30000000-0000-0000-0000-000000000002"),
        Course3 = Guid.Parse("30000000-0000-0000-0000-000000000003"), Course4 = Guid.Parse("30000000-0000-0000-0000-000000000004"),
        Course5 = Guid.Parse("30000000-0000-0000-0000-000000000005");

    // The minimal, stable baseline: three accounts, two courses, one rubric, one knowledge article and one workshop.
    // Integration tests seed against this directly, so its shape and counts are deliberately unchanged from Phase 1–6.
    // A human running the app locally gets this plus SeedExtendedDemoDataAsync below (see Program.cs's --seed handling).
    public static async Task RunAsync(AppDbContext db, UserManager<AppUser> users, RoleManager<IdentityRole<Guid>> roles, IConfiguration config)
    {
        var password = config["Demo:Password"];
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Set Demo__Password to a strong local secret before seeding.");
        foreach (var role in Roles.All)
            if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new(role)));
        var definitions = new[] {
            ("20000000-0000-0000-0000-000000000001", "instructor@demo.teachwell.test", "Alex Morgan", new[] { Roles.Instructor }),
            ("20000000-0000-0000-0000-000000000002", "reviewer@demo.teachwell.test", "Jordan Lee", new[] { Roles.Reviewer, Roles.Instructor }),
            ("20000000-0000-0000-0000-000000000003", "admin@demo.teachwell.test", "Sam Rivera", new[] { Roles.Admin }),
            // D3: read-only results across the college, no course content (ADR 014).
            ("20000000-0000-0000-0000-000000000006", "academic@demo.teachwell.test", "Dana Brooks", new[] { Roles.AcademicAdmin }) };
        foreach (var (id, email, name, assigned) in definitions)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new() { Id = Guid.Parse(id), UserName = email, Email = email, EmailConfirmed = true, TenantId = Tenant, DisplayName = name };
                Check(await users.CreateAsync(user, password));
            }
            foreach (var role in assigned) if (!await users.IsInRoleAsync(user, role)) Check(await users.AddToRoleAsync(user, role));
        }
        var owner = Guid.Parse(definitions[0].Item1);
        var reviewer = Guid.Parse(definitions[1].Item1);
        for (var n = 1; n <= 2; n++)
        {
            var courseId = Guid.Parse($"30000000-0000-0000-0000-{n:000000000000}");
            if (await db.Courses.AnyAsync(c => c.Id == courseId)) continue;
            var moduleId = Guid.NewGuid();
            var course = new Course { Id = courseId, TenantId = Tenant, OwnerId = owner, Code = n == 1 ? "WEB 101" : "DES 120", Title = n == 1 ? "Foundations of Web Development" : "Designing for Everyone", SourceCourseId = $"demo-{n}" };
            course.Elements = [
                new() { Id = moduleId, CourseId = courseId, SourceElementId = "module-1", Type = "module", Title = "Start here", PlainText = "Orientation, practice, and your first project." },
                new() { Id = Guid.NewGuid(), CourseId = courseId, ParentElementId = moduleId, SourceElementId = "page-1", Type = "page", Title = "Welcome and learning goals" },
                new() { Id = Guid.NewGuid(), CourseId = courseId, ParentElementId = moduleId, SourceElementId = "assignment-1", Type = "assignment", Title = "Your first accessible page" },
                // The Canvas syllabus belongs to the course, not to a module, so it has no ParentElementId.
                new() { Id = Guid.NewGuid(), CourseId = courseId, SourceElementId = ReviewVocabulary.SyllabusSourceId, Type = ReviewVocabulary.SyllabusType, Title = "Syllabus" } ];
            // Only the first course is explicitly shared with CTL.
            if (n == 1)
            {
                course.Access.Add(new() { CourseId = courseId, UserId = reviewer, GrantedBy = owner });
                db.AuditLogs.Add(new() { ActorId = owner, EntityId = courseId, SubjectUserId = reviewer, Action = "DemoCourseSharedForReview" });
            }
            db.Courses.Add(course);
        }
        await db.SaveChangesAsync();
        await CaptureSnapshotsAsync(db);
        await SeedRubricAsync(db, reviewer);
        await SeedStandardSetAsync(db, reviewer);
        await SeedSupportResourcesAsync(db, reviewer);
    }

    // Re-reads every Demo-course element and (re)captures a snapshot where content changed or none exists yet; shared by
    // the baseline seed and the extended demo data below, since both can add elements that still need their first snapshot.
    private static async Task CaptureSnapshotsAsync(AppDbContext db)
    {
        var demoElements = await db.Elements.Include(e => e.Snapshots).Where(e => db.Courses.Any(c => c.Id == e.CourseId && c.SourceType == "Demo")).ToListAsync();
        foreach (var element in demoElements.Where(e => e.Type != ReviewVocabulary.CourseType))
        {
            // Fictional HTML runs through the same sanitizer as Canvas imports; older seeds gain evidence and previews through a new
            // appended snapshot. Content a simulated instructor edit has changed (its text no longer matches the demo HTML) is left alone.
            if (DemoHtml.TryGetValue((element.CourseId.ToString()[^1], element.SourceElementId), out var html))
            {
                var (text, evidence) = CanvasText.Normalize(html.Body, null, html.Assignment);
                if (element.Snapshots.Count == 0 || element.PlainText == text || element.PlainText.Length == 0)
                {
                    element.PlainText = text; element.EvidenceJson = evidence.Serialize(); element.PreviewHtml = CanvasText.Preview(html.Body);
                }
            }
            var snapshot = ElementSnapshot.Capture(element, DateTime.UtcNow);
            if (element.Snapshots.OrderByDescending(s => s.FetchedAt).FirstOrDefault()?.ContentHash != snapshot.ContentHash) element.Snapshots.Add(snapshot);
        }
        await db.SaveChangesAsync();
    }

    public static readonly Guid AltTextArticleId = Guid.Parse("50000000-0000-0000-0000-000000000001"),
        RubricWorkshopId = Guid.Parse("51000000-0000-0000-0000-000000000001");

    // Fictional job aid and workshop so a CTL reviewer can attach a real recommendation when demonstrating a support ticket.
    private static async Task SeedSupportResourcesAsync(AppDbContext db, Guid author)
    {
        if (!await db.KnowledgeArticles.AnyAsync(a => a.Id == AltTextArticleId))
        {
            var now = DateTime.UtcNow;
            db.KnowledgeArticles.Add(new()
            {
                Id = AltTextArticleId, TenantId = Tenant, Title = "Writing useful image alt text", Slug = "writing-useful-image-alt-text",
                Body = "Alt text should describe the purpose of an image, not just its contents. Ask: if a student could not see this image, " +
                    "what information would they lose? Describe that. Purely decorative images should be marked decorative instead of given empty alt text " +
                    "as an afterthought. Avoid starting with \"image of\" or \"picture of\" — screen readers already announce that an image is present.",
                TagsJson = System.Text.Json.JsonSerializer.Serialize(new[] { "accessibility", "images" }), Status = ReviewVocabulary.Published,
                AuthorId = author, CreatedAt = now, PublishedAt = now
            });
            db.AuditLogs.Add(new() { ActorId = author, EntityId = AltTextArticleId, Action = "KnowledgeArticleCreated" });
        }
        if (!await db.Workshops.AnyAsync(w => w.Id == RubricWorkshopId))
        {
            var start = DateTime.UtcNow.Date.AddDays(14).AddHours(16);
            db.Workshops.Add(new()
            {
                Id = RubricWorkshopId, TenantId = Tenant, Title = "Writing clear assignment rubrics", Description = "A hands-on session on attaching rubrics with descriptive performance levels to Canvas assignments.",
                StartsAt = start, EndsAt = start.AddHours(1), Modality = WorkshopVocabulary.Online, Capacity = 20, CreatedBy = author
            });
            db.AuditLogs.Add(new() { ActorId = author, EntityId = RubricWorkshopId, Action = "WorkshopCreated" });
        }
        await db.SaveChangesAsync();
    }

    // Deliberate, detectable issues let the offline demo exercise deterministic checks across every seeded course.
    private static readonly Dictionary<(char Course, string Element), (string Body, AssignmentEvidence? Assignment)> DemoHtml = new()
    {
        [('1', "page-1")] = ("<h2>Welcome</h2><p>In this course you will build an accessible page, explain your design choices, and reflect on your process. This is fictional demo content.</p>" +
            "<img src=\"/demo/welcome-banner.png\" alt=\"welcome-banner.png\"><h4>What you will learn</h4><ul><li>Structure pages with semantic headings</li><li>Write descriptive links</li></ul>" +
            "<p>Review the weekly schedule: <a href=\"https://schedule.example.org/web101\">click here</a>.</p><h3></h3>" +
            // One uncaptioned player (a definite finding) and one embed (a confirmation step), so both media checks have a worked example.
            "<video controls title=\"Course walkthrough\"><source src=\"/demo/web101-walkthrough.mp4\"></video>" +
            "<iframe title=\"Semantic HTML in 5 minutes\" src=\"https://www.youtube-nocookie.com/embed/demo-fictional\"></iframe>", null),
        [('1', "assignment-1")] = ("<p>Create a page with semantic headings, descriptive links, and useful image alternatives. Submit a short reflection. No student submissions are stored in TeachWell.</p>" +
            "<img src=\"/demo/sample-layout.png\"><table><tr><td>Part</td><td>Points</td></tr><tr><td>Page</td><td>8</td></tr><tr><td>Reflection</td><td>2</td></tr></table>",
            new(HasRubric: false, PointsPossible: 10, HasDueDate: false)),
        // WEB 101's syllabus deliberately omits the accessibility statement, so the demo has one open required element.
        [('1', ReviewVocabulary.SyllabusSourceId)] = ("<h2>WEB 101: Foundations of Web Development</h2><p>This is fictional demo content.</p>" +
            "<h3>Contacting me</h3><p>Email me through Canvas; my office hours are Tuesdays 2–4pm. I normally respond within one working day.</p>" +
            "<h3>Grading</h3><p>The grade breakdown is 60% projects, 25% labs and 15% participation, on the college grading scale.</p>" +
            "<h3>Deadlines</h3><p>Late work loses 10% per day for up to five days. Tell me before the deadline if something has gone wrong.</p>" +
            "<h3>Academic integrity</h3><p>Submitted work must be your own work, with sources cited. Ask me before using an AI tool for a graded task.</p>", null),
        // DES 120's syllabus covers everything the defaults look for, so the step shows as finished there.
        [('2', ReviewVocabulary.SyllabusSourceId)] = ("<h2>DES 120: Designing for Everyone</h2><p>This is fictional demo content.</p>" +
            "<h3>Contacting me</h3><p>Email me through Canvas; student hours are Thursdays 10am–12pm.</p>" +
            "<h3>Accessibility</h3><p>If you need an accommodation, contact Disability Services and let me know what helps. Tell me about any barrier in this course, whether or not you have documentation.</p>" +
            "<h3>Grading</h3><p>The grading scale and grade breakdown are on the course home page: 50% studio work, 30% critique, 20% reflection.</p>" +
            "<h3>Late work</h3><p>Every project has a 48-hour grace period. After that, talk to me about an extension.</p>" +
            "<h3>Academic integrity</h3><p>Credit every source you build on, including images. Submitted work must be your own work.</p>", null),
        [('2', "page-1")] = ("<h2>Welcome</h2><p>Design with every learner in mind. This is fictional demo content.</p>" +
            "<img src=\"/demo/wireframes.png\" alt=\"Two learners sketching wireframes together at a table\"><h3>Getting started</h3>" +
            "<p>Read the <a href=\"/courses/demo-2/pages/syllabus\">course syllabus and grading policy</a> before the first session.</p>", null),
        [('2', "assignment-1")] = ("<h3>Task</h3><p>Audit a fictional landing page and propose three inclusive design improvements, with a short rationale for each.</p>" +
            "<table><tr><th>Criterion</th><th>Points</th></tr><tr><td>Findings</td><td>6</td></tr><tr><td>Rationale</td><td>4</td></tr></table>",
            new(HasRubric: true, PointsPossible: 10, HasDueDate: true, DueAt: DemoDue(2))),
        [('3', "page-1")] = ("<h2>Welcome</h2><p>This course introduces reading, questioning and communicating with data. This is fictional demo content.</p>" +
            "<img src=\"/demo/chart-intro.png\" alt=\"chart-intro.png\"><h3>What you will learn</h3><ul><li>Read common chart types without being misled</li><li>Ask where a dataset came from</li></ul>" +
            "<p>Review the <a href=\"https://schedule.example.org/data150\">weekly schedule</a> before the first session.</p>", null),
        [('3', "page-2")] = ("<h2>Reading data visualizations</h2><p>Charts can mislead as easily as they inform. This is fictional demo content.</p>" +
            "<img src=\"/demo/misleading-axis.png\"><h4>Common pitfalls</h4><p>Watch for truncated axes, cherry-picked ranges, and correlation presented as causation.</p>" +
            "<p>For more background, <a href=\"https://data-ethics.example.org/charts\">click here</a>.</p>", null),
        [('3', "page-3")] = ("<h2>Finding trustworthy datasets</h2><p>Before you chart a dataset, find out who published it and why. This is fictional demo content.</p>" +
            "<h3>Questions to ask</h3><ol><li>Who collected the data, and how?</li><li>When was it last updated?</li><li>What is missing from it?</li></ol>" +
            "<img src=\"/demo/open-data-portal.png\" alt=\"A city open-data portal listing transit, housing and budget datasets\">" +
            "<p>Browse the <a href=\"https://data.example.org/portal\">city open-data portal</a> for this week's practice.</p>", null),
        [('3', "assignment-1")] = ("<p>Choose a public dataset, build one chart that answers a specific question, and write a two-sentence caption explaining what it shows. No student submissions are stored in TeachWell.</p>" +
            "<table><tr><th>Part</th><th>Points</th></tr><tr><td>Chart</td><td>15</td></tr><tr><td>Caption</td><td>5</td></tr></table>",
            new(HasRubric: true, PointsPossible: 20, HasDueDate: true, DueAt: DemoDue(9))),
        [('3', "assignment-2")] = ("<h3></h3><p>Reflect on a dataset you encountered this week. Who collected it, who benefits, and who could be harmed by its use? This is fictional demo content.</p>" +
            "<table><tr><td>Criterion</td><td>Notes</td></tr><tr><td>Consent</td><td></td></tr></table>",
            new(HasRubric: false, PointsPossible: 10, HasDueDate: true, DueAt: DemoDue(16))),
        [('4', "page-1")] = ("<h2>Why inclusive assessment matters</h2><p>Assessment design choices affect who can show what they know. This is fictional demo content.</p>" +
            "<img src=\"/demo/assessment-design.png\" alt=\"assessment-design.png\">" +
            "<p>See the <a href=\"https://udl.example.org/assessment\">UDL guidelines on assessment</a> for background.</p>", null),
        [('4', "assignment-1")] = ("<p>Take an existing rubric and redesign it so criteria describe observable work rather than assumed background knowledge. No student submissions are stored in TeachWell.</p>" +
            "<table><tr><th>Criterion</th><th>Points</th></tr><tr><td>Redesigned rubric</td><td>10</td></tr><tr><td>Rationale</td><td>5</td></tr></table>",
            new(HasRubric: true, PointsPossible: 15, HasDueDate: true, DueAt: DemoDue(9))),
        [('5', "page-1")] = ("<h2>What counts as evidence?</h2><p>Teaching decisions are stronger when they rest on more than intuition. This is fictional demo content.</p>" +
            "<img src=\"/demo/evidence-ladder.png\" alt=\"evidence-ladder.png\"><h3>Kinds of evidence</h3><ul><li>Published research</li><li>Your own course data</li><li>Student feedback</li></ul>" +
            "<p>Start with the <a href=\"https://library.example.org/edu210\">here</a> reading list.</p>", null),
        [('5', "assignment-1")] = ("<p>Design one lesson and explain which evidence informed each choice. No student submissions are stored in TeachWell.</p>" +
            "<table><tr><td>Part</td><td>Points</td></tr><tr><td>Lesson plan</td><td>12</td></tr><tr><td>Evidence rationale</td><td>8</td></tr></table>",
            new(HasRubric: false, PointsPossible: 20, HasDueDate: true, DueAt: DemoDue(23))),
    };

    // Fixed fictional deadlines (11:59 pm Mountain, stored in UTC) in November 2026, so pacing checks have dates to work with.
    private static DateTime DemoDue(int day) => new(2026, 11, day, 6, 59, 0, DateTimeKind.Utc);

    public static readonly Guid RubricProfileId = Guid.Parse("40000000-0000-0000-0000-000000000001"), RubricVersionId = Guid.Parse("41000000-0000-0000-0000-000000000001");

    // Original TeachWell wording informed by public design principles; not copied from any proprietary rubric.
    private static async Task SeedRubricAsync(AppDbContext db, Guid author)
    {
        if (await db.RubricProfiles.AnyAsync(p => p.Id == RubricProfileId)) return;
        string[] both = ["page", "assignment"], assignment = ["assignment"];
        var criteria = new (string Code, string Title, string Category, string[] Types, string Method, int Weight, bool Required, string[] Checks, string Guidance, string[] Anchors)[] {
            ("STR-1", "Purpose and next steps are clear", "Course structure and navigation", both, "Manual", 1, true, [],
                "Quote the sentence that states what learners do next, or note its absence.",
                ["Purpose or next step is missing.", "Purpose is implied but next steps require guesswork.", "Purpose and next steps are stated clearly.", "Purpose, next steps and the connection to the module are explicit."]),
            ("OBJ-1", "Outcomes are observable and measurable", "Objectives and competencies", both, "Manual", 2, true, [],
                "Cite the outcome statement and the action verb used.",
                ["No outcome is stated.", "Outcome uses vague verbs such as understand or know.", "Outcome uses an observable, measurable verb.", "Outcome is measurable and explicitly aligned to course competencies."]),
            ("ASM-1", "Instructions state deliverables and evaluation criteria", "Assessment and feedback", assignment, "Manual", 2, true, [],
                "List the deliverables and criteria the instructions mention.",
                ["Deliverables are unclear.", "Deliverables are stated but evaluation criteria are not.", "Deliverables and evaluation criteria are stated.", "Deliverables, criteria and an example of quality work are provided."]),
            ("ASM-2", "An evaluation rubric is attached", "Assessment and feedback", assignment, "Assisted", 1, true, [DeterministicChecks.RubricMissing],
                "The automated check reads Canvas rubric metadata; confirm the rubric matches the instructions.",
                ["No rubric or criteria are available.", "Criteria exist but are not attached as a rubric.", "A rubric is attached and matches the task.", "The rubric includes performance levels with descriptive anchors."]),
            ("ACC-1", "Images have meaningful text alternatives", "Accessibility and UDL", both, "Assisted", 2, true,
                [DeterministicChecks.ImageAltMissing, DeterministicChecks.ImageAltFilename, DeterministicChecks.ImageAltEmpty],
                "Automated checks find missing or file-name alt text; judge whether existing text conveys purpose.",
                ["Informative images lack alternatives.", "Some alternatives are missing or uninformative.", "All informative images have meaningful alternatives.", "Alternatives are concise and purposeful; complex images have long descriptions."]),
            ("ACC-2", "Headings form a logical outline", "Accessibility and UDL", both, "Assisted", 1, false, [DeterministicChecks.HeadingEmpty, DeterministicChecks.HeadingSkipped],
                "Automated checks detect empty headings and skipped levels.",
                ["Headings are absent or used only for styling.", "Headings exist but skip levels or are empty.", "Headings follow a logical order.", "Headings create a clear, scannable outline matching the content."]),
            ("ACC-3", "Link text describes the destination", "Accessibility and UDL", both, "Assisted", 1, false, [DeterministicChecks.LinkVague, DeterministicChecks.LinkEmpty],
                "Automated checks flag vague or empty link text; confirm destinations are clear.",
                ["Links are empty or meaningless out of context.", "Some links use vague text such as click here.", "Link text describes each destination.", "Link text is descriptive and signals file types or external sites where helpful."]),
            ("ACC-4", "Data tables identify header cells", "Accessibility and UDL", both, "Assisted", 1, false, [DeterministicChecks.TableHeaders],
                "The automated check detects tables without header cells.",
                ["Data tables have no headers.", "Headers are partially marked.", "Header cells are marked for each data table.", "Tables have headers and a caption explaining their purpose."]),
            ("TEC-1", "Content is available to learners", "Technical quality", both, "Assisted", 1, true, [DeterministicChecks.ItemUnavailable, DeterministicChecks.ItemUnpublished],
                "The automated check reads the captured Canvas status; confirm intended visibility.",
                ["Content is missing or unavailable.", "Content exists but is unpublished or partially unavailable.", "Content is available as intended.", "Content is available and its availability dates are communicated."]),
            ("ENG-1", "Learners practice and receive feedback", "Engagement and active learning", both, "Manual", 1, false, [],
                "Describe the practice or feedback opportunity, if any.",
                ["No practice or feedback opportunity.", "Practice exists without feedback.", "Practice includes timely feedback.", "Practice, feedback and self-checks build toward the assessment."]),
        };
        // Insert as a draft, then publish, so database triggers can reject criteria added to published versions.
        var version = new RubricVersion { Id = RubricVersionId, ProfileId = RubricProfileId, VersionNumber = 1, CreatedBy = author };
        version.Criteria = criteria.Select((d, i) => new RubricCriterion {
            Id = Guid.Parse($"42000000-0000-0000-0000-{i + 1:000000000000}"), VersionId = RubricVersionId, Position = i + 1, Code = d.Code, Title = d.Title, Category = d.Category,
            Description = d.Title + ".", ElementTypesJson = JsonSerializer.Serialize(d.Types), ReviewMethod = d.Method, Weight = d.Weight, Required = d.Required,
            AutomatedChecksJson = JsonSerializer.Serialize(d.Checks), EvidenceGuidance = d.Guidance, RatingAnchorsJson = JsonSerializer.Serialize(d.Anchors) }).ToList();
        db.RubricProfiles.Add(new RubricProfile { Id = RubricProfileId, TenantId = Tenant, Name = "Course design essentials (demo)", DeliveryMethod = "Any", CreatedBy = author, Versions = [version] });
        await db.SaveChangesAsync();
        version.Status = ReviewVocabulary.Published; version.PublishedBy = author; version.PublishedAt = DateTime.UtcNow; version.Revision = Guid.NewGuid();
        db.AuditLogs.Add(new() { ActorId = author, EntityId = RubricVersionId, Action = "DemoRubricPublished" });
        await db.SaveChangesAsync();
    }

    // Everything below is extra: two more personas, two more courses, more knowledge/workshop content, and worked
    // examples of reviews, revision tasks, support tickets and competencies across every status each state machine
    // supports. Called only from the real `--seed` CLI path (see Program.cs), never from the test suite's baseline
    // seed, so test fixtures stay exactly as predictable as they were before this richer demo data existed.
    public static async Task SeedExtendedDemoDataAsync(AppDbContext db, UserManager<AppUser> users, RoleManager<IdentityRole<Guid>> roles, IConfiguration config)
    {
        var password = config["Demo:Password"];
        if (string.IsNullOrWhiteSpace(password)) throw new InvalidOperationException("Set Demo__Password to a strong local secret before seeding.");
        var definitions = new[] {
            // A second instructor who never interacts with Jordan Lee's courses, and a second, Reviewer-only CTL account,
            // so course access and the triage queue can be shown to work per reviewer, not per role.
            ("20000000-0000-0000-0000-000000000004", "instructor2@demo.teachwell.test", "Priya Chen", new[] { Roles.Instructor }),
            ("20000000-0000-0000-0000-000000000005", "reviewer2@demo.teachwell.test", "Morgan Blake", new[] { Roles.Reviewer }) };
        var people = new Dictionary<string, CurrentUser>();
        foreach (var (id, email, name, assigned) in definitions)
        {
            var user = await users.FindByEmailAsync(email);
            if (user is null)
            {
                user = new() { Id = Guid.Parse(id), UserName = email, Email = email, EmailConfirmed = true, TenantId = Tenant, DisplayName = name };
                Check(await users.CreateAsync(user, password));
            }
            foreach (var role in assigned) if (!await users.IsInRoleAsync(user, role)) Check(await users.AddToRoleAsync(user, role));
            people[name] = new CurrentUser(Guid.Parse(id), Tenant, name, email, assigned);
        }
        foreach (var baseName in new[] { "Alex Morgan", "Jordan Lee", "Sam Rivera" })
        {
            var user = await users.FindByEmailAsync(baseName switch { "Alex Morgan" => "instructor@demo.teachwell.test", "Jordan Lee" => "reviewer@demo.teachwell.test", _ => "admin@demo.teachwell.test" })
                ?? throw new InvalidOperationException("Run the baseline seed before the extended demo data seed.");
            people[baseName] = new CurrentUser(user.Id, Tenant, user.DisplayName, user.Email!, (await users.GetRolesAsync(user)).ToArray());
        }
        var owner = people["Alex Morgan"].Id;
        var morganId = people["Morgan Blake"].Id;

        // Morgan Blake, the second CTL reviewer, is pre-shared on both of Alex Morgan's courses so there is real, browsable
        // review history from day one without touching Jordan Lee's own README-documented "start a review" walkthrough.
        foreach (var courseId in new[] { Course1, Course2 })
            if (!await db.CourseAccess.AnyAsync(a => a.CourseId == courseId && a.UserId == morganId))
            {
                db.CourseAccess.Add(new() { CourseId = courseId, UserId = morganId, GrantedBy = owner });
                db.AuditLogs.Add(new() { ActorId = owner, EntityId = courseId, SubjectUserId = morganId, Action = "DemoCourseSharedForReview" });
            }
        await SeedMoreCoursesAsync(db, people);
        await SeedJordanCourseAsync(db, people);
        await db.SaveChangesAsync();
        await SeedModuleVarietyAsync(db);
        await CaptureSnapshotsAsync(db);
        await SeedCourseRubricAsync(db, morganId);
        // Before any seeded review starts, so seeded reviews pin the set their course selects.
        await SeedOnlineStandardsAsync(db, morganId);
        await SeedExtraSupportResourcesAsync(db, people);
        await SeedReviewsAndTasksAsync(db, people);
        await SeedSupportTicketsAsync(db, people);
        await SeedCompetenciesAsync(db, people);
        // Course structure includes competency alignment, so course-level reviews are seeded after competencies.
        await SeedCourseReviewsAsync(db, people);
        // Last, so the reviews above stay pinned to v1 and the demo shows earlier results keeping the version they ran under.
        await SeedStandardsUpgradeAsync(db, morganId);
        await SeedGuideResourcesAsync(db, morganId);
        await SeedProgramsAsync(db);
        await SeedReadinessAsync(db, people);
        // Oversight figures for every demo course, as a CTL "re-check all courses" would produce (ADR 014).
        await new GuideService(db).RecordAllAsync(Tenant, default);
    }

    // Fictional programs so oversight can be filtered by program as well as department (ADR 014 amendment). Only fills blanks.
    private static async Task SeedProgramsAsync(AppDbContext db)
    {
        var programs = new Dictionary<Guid, string> { [Course1] = "Web Development AAS", [Course2] = "Web Development AAS", [Course3] = "Data Analytics Certificate" };
        foreach (var course in await db.Courses.Where(c => programs.Keys.Contains(c.Id) && c.Program == null).ToListAsync())
            course.Program = programs[course.Id];
        await db.SaveChangesAsync();
    }

    // Phase 10 demo: WEB 101 endorsed by Morgan Blake against the standards that apply to it, and DATA 150 waiting in the CTL queue.
    // Each request pins the course's real guide state at seed time, as a real request would.
    private static async Task SeedReadinessAsync(AppDbContext db, Dictionary<string, CurrentUser> people)
    {
        var alex = people["Alex Morgan"]; var priya = people["Priya Chen"]; var morgan = people["Morgan Blake"].Id;
        async Task<ReadinessRequest> Request(Guid courseId, CurrentUser requester, DateTime at, string note)
        {
            var guide = (await new GuideService(db).GetAsync(requester, courseId, default)).Value!;
            var ids = await db.Elements.AsNoTracking().Where(e => e.CourseId == courseId && e.Type != ReviewVocabulary.CourseType && e.SourceStatus != "Removed").Select(e => e.Id).ToListAsync();
            var snapshots = await db.ElementSnapshots.AsNoTracking().Where(x => ids.Contains(x.ElementId)).GroupBy(x => x.ElementId)
                .Select(g => new { g.Key, Id = g.OrderByDescending(x => x.FetchedAt).Select(x => x.Id).First() }).ToDictionaryAsync(x => x.Key, x => x.Id);
            return new()
            {
                Id = Guid.NewGuid(), TenantId = Tenant, CourseId = courseId, RequestedBy = requester.Id, RequestedAt = at, Note = note,
                StandardSetVersionId = guide.Standards.VersionId, StandardsName = guide.Standards.Name, StandardsVersionNumber = guide.Standards.VersionNumber,
                ContentAsOf = guide.ContentAsOf, SnapshotIdsJson = System.Text.Json.JsonSerializer.Serialize(snapshots),
                GuideSummaryJson = ReadinessService.Summarize(guide).Serialize()
            };
        }
        var now = DateTime.UtcNow;
        if (!await db.ReadinessRequests.AnyAsync(r => r.CourseId == Course1))
        {
            var request = await Request(Course1, alex, now.AddDays(-3), "Ready for the fall term; all required guide steps are done.");
            request.Status = ReadinessVocabulary.Endorsed; request.ReviewerId = morgan; request.TakenAt = now.AddDays(-2); request.DecidedAt = now.AddDays(-1);
            request.DecisionSummary = "Clear start module, accessible pages and a rubric on the first project. Remaining guide steps are recommendations.";
            db.ReadinessRequests.Add(request);
            db.Endorsements.Add(new()
            {
                Id = Guid.NewGuid(), TenantId = Tenant, CourseId = Course1, RequestId = request.Id, StandardSetVersionId = request.StandardSetVersionId,
                StandardsName = request.StandardsName, StandardsVersionNumber = request.StandardsVersionNumber, ContentAsOf = request.ContentAsOf,
                SnapshotIdsJson = request.SnapshotIdsJson, ReviewerId = morgan, EndorsedAt = request.DecidedAt.Value, ValidUntil = request.DecidedAt.Value.AddMonths(36),
                Summary = request.DecisionSummary
            });
            db.AuditLogs.Add(new() { ActorId = morgan, EntityId = request.Id, Action = "ReadinessEndorsed" });
            NotificationService.Add(db, Tenant, [alex.Id], NotificationVocabulary.ReadinessEndorsed, "WEB 101 was endorsed by the CTL",
                $"Morgan Blake: {request.DecisionSummary}", Course1, request.Id);
        }
        if (!await db.ReadinessRequests.AnyAsync(r => r.CourseId == Course3))
        {
            var waiting = await Request(Course3, priya, now.AddDays(-1), "First time teaching this online; I would value a second pair of eyes.");
            db.ReadinessRequests.Add(waiting);
            db.AuditLogs.Add(new() { ActorId = priya.Id, EntityId = waiting.Id, Action = "ReadinessRequested" });
            NotificationService.Add(db, Tenant, await NotificationService.UsersInRoleAsync(db, Tenant, Roles.Reviewer, default), NotificationVocabulary.ReadinessRequested,
                "Readiness review requested: DATA 150", "Priya Chen asked the CTL for a readiness review of DATA 150 · Introduction to Data Literacy. Take it from the Readiness queue.",
                Course3, waiting.Id);
        }
        await db.SaveChangesAsync();
    }

    // The CTL's job aid and workshop offered on the guide steps they help with, so the demo guide shows "Learn more from the CTL".
    private static async Task SeedGuideResourcesAsync(AppDbContext db, Guid author)
    {
        (string Code, Guid? Article, Guid? Workshop)[] links =
        [
            (DeterministicChecks.ImageAltMissing, AltTextArticleId, null), (DeterministicChecks.ImageAltFilename, AltTextArticleId, null),
            (DeterministicChecks.RubricMissing, null, RubricWorkshopId),
        ];
        foreach (var (code, article, workshop) in links)
        {
            if (await db.CheckResourceLinks.AnyAsync(l => l.TenantId == Tenant && l.CheckCode == code && l.ArticleId == article && l.WorkshopId == workshop)) continue;
            db.CheckResourceLinks.Add(new() { Id = Guid.NewGuid(), TenantId = Tenant, CheckCode = code, ArticleId = article, WorkshopId = workshop, CreatedBy = author });
        }
        await db.SaveChangesAsync();
    }

    // DATA 150 gains two more modules: one following the "Week N:" convention with a new page, and one breaking it and left
    // empty, so a course-level review has real naming and empty-module issues to find.
    private static async Task SeedModuleVarietyAsync(AppDbContext db)
    {
        if (await db.Elements.AnyAsync(e => e.CourseId == Course3 && e.Type == "module" && e.SourceElementId == "module-2")) return;
        var week2 = Guid.NewGuid();
        db.Elements.AddRange(
            new CourseElement { Id = week2, CourseId = Course3, SourceElementId = "module-2", Type = "module", Title = "Week 2: Working with real datasets", Position = 1 },
            new CourseElement { Id = Guid.NewGuid(), CourseId = Course3, ParentElementId = week2, SourceElementId = "page-3", Type = "page", Title = "Finding trustworthy datasets" },
            new CourseElement { Id = Guid.NewGuid(), CourseId = Course3, SourceElementId = "module-3", Type = "module", Title = "Data storytelling", Position = 2 });
        await db.SaveChangesAsync();
    }

    public static readonly Guid CourseRubricProfileId = Guid.Parse("40000000-0000-0000-0000-000000000002"), CourseRubricVersionId = Guid.Parse("41000000-0000-0000-0000-000000000002");

    // Course-level criteria from REVIEW_FRAMEWORK.md's structure, alignment and pacing guidance, plus reviewer-judged design
    // criteria (visual consistency, navigation, workload) that the course review's page previews support.
    private static async Task SeedCourseRubricAsync(AppDbContext db, Guid author)
    {
        if (await db.RubricProfiles.AnyAsync(p => p.Id == CourseRubricProfileId)) return;
        string[] course = [ReviewVocabulary.CourseType];
        string[] Anchors(string zero, string one, string two, string three) => [zero, one, two, three];
        var criteria = new (string Code, string Title, string Category, string Method, int Weight, bool Required, string[] Checks, string Guidance, string[] Anchors)[] {
            ("CRS-1", "Course has a clear starting point", "Course structure and navigation", "Assisted", 2, true, [CourseChecks.StartMissing],
                "The check looks for an orientation module or first item; judge whether it actually tells students how the course works.",
                Anchors("No starting point.", "A start item exists but does not orient students.", "A start item explains organization and first steps.", "Orientation also sets expectations, support and communication norms.")),
            ("CRS-2", "Modules are consistently named and organized", "Course structure and navigation", "Assisted", 1, true, [CourseChecks.ModuleNaming, CourseChecks.ModuleEmpty],
                "Checks flag naming that breaks the course's pattern and empty modules; judge whether modules share an internal layout.",
                Anchors("Modules have no discernible organization.", "Naming or layout varies noticeably.", "Modules follow one naming and layout pattern.", "Every module opens with an overview and follows the same sequence.")),
            ("CRS-3", "All content is reachable and available", "Technical quality", "Assisted", 1, false, [CourseChecks.ContentUnplaced, CourseChecks.ModuleUnpublished],
                "Checks flag pages and assignments outside modules and unpublished modules; confirm whether each is intentional.",
                Anchors("Important content is unreachable.", "Some content is hidden or only reachable by link.", "All content is reachable through modules.", "Content is reachable and availability dates are communicated.")),
            ("CRS-4", "Assignments have due dates that support pacing", "Assessment and feedback", "Assisted", 1, true, [CourseChecks.DueDateMissing],
                "The check reads due-date metadata; judge whether the workload is spread sensibly across the term.",
                Anchors("No due dates.", "Some assignments lack due dates.", "All assignments have due dates.", "Due dates are paced and visible in a course schedule.")),
            ("CRS-5", "Every assignment is aligned to a course competency", "Objectives and competencies", "Assisted", 2, true, [CourseChecks.Unaligned, CourseChecks.CompetenciesMissing],
                "Checks use the course's competency mappings; judge whether each mapping is credible.",
                Anchors("No competencies or alignment.", "Some assignments are unaligned.", "Every assignment is aligned.", "Alignment is complete and every competency has assessment evidence.")),
            ("CRS-6", "Visual design is consistent across pages", "Course structure and navigation", "Manual", 1, false, [],
                "Open each page's preview from the course outline. Compare heading levels, banners or templates, emphasis and layout patterns.",
                Anchors("Pages look unrelated to one another.", "Design varies noticeably between pages.", "Pages share a consistent design pattern.", "A consistent template makes every page instantly familiar.")),
            ("CRS-7", "Navigation between modules is predictable", "Course structure and navigation", "Manual", 1, false, [],
                "Walk through the course in module order. Note anywhere a student would have to guess where to go next.",
                Anchors("Navigation requires guesswork throughout.", "Next steps are unclear in places.", "Students can move through the course predictably.", "Every module ends by pointing to the next step.")),
            ("CRS-8", "Workload is balanced across modules", "Instructional content and scaffolding", "Manual", 1, false, [],
                "Compare the number and size of items per module against the course's credit hours and schedule.",
                Anchors("Workload is severely uneven.", "Some modules are noticeably heavier.", "Workload is reasonably balanced.", "Workload is balanced and estimated time is stated per module.")),
        };
        var version = new RubricVersion { Id = CourseRubricVersionId, ProfileId = CourseRubricProfileId, VersionNumber = 1, CreatedBy = author };
        version.Criteria = criteria.Select((d, i) => new RubricCriterion {
            Id = Guid.Parse($"42000000-0000-0000-0001-{i + 1:000000000000}"), VersionId = CourseRubricVersionId, Position = i + 1, Code = d.Code, Title = d.Title, Category = d.Category,
            Description = d.Title + ".", ElementTypesJson = JsonSerializer.Serialize(course), ReviewMethod = d.Method, Weight = d.Weight, Required = d.Required,
            AutomatedChecksJson = JsonSerializer.Serialize(d.Checks), EvidenceGuidance = d.Guidance, RatingAnchorsJson = JsonSerializer.Serialize(d.Anchors) }).ToList();
        db.RubricProfiles.Add(new RubricProfile { Id = CourseRubricProfileId, TenantId = Tenant, Name = "Course-level design (demo)", DeliveryMethod = "Any", CreatedBy = author, Versions = [version] });
        await db.SaveChangesAsync();
        version.Status = ReviewVocabulary.Published; version.PublishedBy = author; version.PublishedAt = DateTime.UtcNow; version.Revision = Guid.NewGuid();
        db.AuditLogs.Add(new() { ActorId = author, EntityId = CourseRubricVersionId, Action = "DemoRubricPublished" });
        await db.SaveChangesAsync();
    }

    // Course-level worked examples: a published review of WEB 101 and of DATA 150 (whose extra modules trip the naming and
    // empty-module checks), plus a revision task linked to the empty module so its Canvas location is one click away.
    private static async Task SeedCourseReviewsAsync(AppDbContext db, Dictionary<string, CurrentUser> people)
    {
        var reviews = new ReviewService(db);
        var tasks = new TaskService(db);
        var morgan = people["Morgan Blake"];
        async Task<ReviewDto?> PublishedCourseReview(Guid courseId, string[] pattern, string summary)
        {
            if (await db.Reviews.AnyAsync(r => r.CourseId == courseId && r.ReviewerId == morgan.Id && db.Elements.Any(e => e.Id == r.ElementId && e.Type == ReviewVocabulary.CourseType))) return null;
            var review = (await reviews.StartCourseReviewAsync(morgan, courseId, new StartReviewInput(CourseRubricVersionId), default)).Value;
            if (review is null) return null;
            for (var i = 0; i < review.Evaluations.Count; i++)
                review = (await reviews.EvaluateAsync(morgan, review.Id, review.Evaluations[i].CriterionId,
                    new EvaluationInput(review.Revision, ReviewVocabulary.Rated, int.Parse(pattern[i % pattern.Length]), "", ""), default)).Value!;
            review = (await reviews.RunChecksAsync(morgan, review.Id, review.Revision, default)).Value!;
            foreach (var finding in review.Findings.Where(f => f.Status == ReviewVocabulary.Proposed).ToList())
                review = (await reviews.UpdateFindingAsync(morgan, finding.Id, new FindingUpdateInput(review.Revision, ReviewVocabulary.Confirmed, null, null, null, null, null), default)).Value!;
            return (await reviews.PublishAsync(morgan, review.Id, new PublishReviewInput(review.Revision, summary), default)).Value;
        }
        await PublishedCourseReview(Course1, ["3", "3", "3", "1", "3", "2", "3", "2"], "Clear structure; set a due date on the first assignment.");
        if (await PublishedCourseReview(Course3, ["3", "1", "3", "3", "1", "2", "2", "2"], "Good orientation; module naming drifts and one module is still empty.") is { } data150
            && data150.Findings.FirstOrDefault(f => f.CheckCode == CourseChecks.ModuleEmpty) is { } empty
            && !await db.RevisionTasks.AnyAsync(t => t.Title == "Fill or remove the empty \"Data storytelling\" module"))
        {
            var task = (await tasks.CreateAsync(morgan, new CreateTaskInput(Course3, "Fill or remove the empty \"Data storytelling\" module",
                "Students who open this module find nothing in it. Add its content, or remove it until it is ready.", "Low", null, [empty.Id]), default)).Value;
            if (task is not null) await tasks.TransitionAsync(morgan, task.Id, new TaskTransitionInput(task.Revision, TaskVocabulary.NeedsRevision, null), default);
        }
    }

    // Two more instructor-owned courses: one shared with both CTL reviewers (more review/task/ticket material to browse),
    // and one left unshared so the course-sharing workflow still has a real, un-demonstrated course to try it on.
    private static async Task SeedMoreCoursesAsync(AppDbContext db, Dictionary<string, CurrentUser> people)
    {
        if (await db.Courses.AnyAsync(c => c.Id == Course3)) return;
        var priya = people["Priya Chen"].Id;
        var jordan = people["Jordan Lee"].Id;
        var morgan = people["Morgan Blake"].Id;
        var c3Module = Guid.NewGuid();
        var course3 = new Course { Id = Course3, TenantId = Tenant, OwnerId = priya, Code = "DATA 150", Title = "Introduction to Data Literacy", SourceCourseId = "demo-3" };
        course3.Elements = [
            new() { Id = c3Module, CourseId = Course3, SourceElementId = "module-1", Type = "module", Title = "Start here", PlainText = "Orientation, datasets, and your first visualization." },
            new() { Id = Guid.NewGuid(), CourseId = Course3, ParentElementId = c3Module, SourceElementId = "page-1", Type = "page", Title = "Welcome to data literacy" },
            new() { Id = Guid.NewGuid(), CourseId = Course3, ParentElementId = c3Module, SourceElementId = "page-2", Type = "page", Title = "Reading data visualizations" },
            new() { Id = Guid.NewGuid(), CourseId = Course3, ParentElementId = c3Module, SourceElementId = "assignment-1", Type = "assignment", Title = "Build a chart from open data" },
            new() { Id = Guid.NewGuid(), CourseId = Course3, ParentElementId = c3Module, SourceElementId = "assignment-2", Type = "assignment", Title = "Data ethics reflection" } ];
        course3.Access.Add(new() { CourseId = Course3, UserId = jordan, GrantedBy = priya });
        course3.Access.Add(new() { CourseId = Course3, UserId = morgan, GrantedBy = priya });
        db.AuditLogs.Add(new() { ActorId = priya, EntityId = Course3, SubjectUserId = jordan, Action = "DemoCourseSharedForReview" });
        db.AuditLogs.Add(new() { ActorId = priya, EntityId = Course3, SubjectUserId = morgan, Action = "DemoCourseSharedForReview" });
        db.Courses.Add(course3);

        var c4Module = Guid.NewGuid();
        var course4 = new Course { Id = Course4, TenantId = Tenant, OwnerId = priya, Code = "ASMT 210", Title = "Inclusive Assessment Design", SourceCourseId = "demo-4" };
        course4.Elements = [
            new() { Id = c4Module, CourseId = Course4, SourceElementId = "module-1", Type = "module", Title = "Start here", PlainText = "Orientation and assessment redesign practice." },
            new() { Id = Guid.NewGuid(), CourseId = Course4, ParentElementId = c4Module, SourceElementId = "page-1", Type = "page", Title = "Why inclusive assessment matters", SourceStatus = "Unpublished" },
            new() { Id = Guid.NewGuid(), CourseId = Course4, ParentElementId = c4Module, SourceElementId = "assignment-1", Type = "assignment", Title = "Redesign a rubric for universal design" } ];
        db.Courses.Add(course4);
    }

    // Jordan Lee holds both roles, so their Instructor view needs a course of their own; Morgan Blake is shared on it to review it.
    private static async Task SeedJordanCourseAsync(AppDbContext db, Dictionary<string, CurrentUser> people)
    {
        if (await db.Courses.AnyAsync(c => c.Id == Course5)) return;
        var jordan = people["Jordan Lee"].Id;
        var morgan = people["Morgan Blake"].Id;
        var module = Guid.NewGuid();
        var course = new Course { Id = Course5, TenantId = Tenant, OwnerId = jordan, Code = "EDU 210", Title = "Teaching with Evidence", SourceCourseId = "demo-5" };
        course.Elements = [
            new() { Id = module, CourseId = Course5, SourceElementId = "module-1", Type = "module", Title = "Start here", PlainText = "Orientation and your first evidence-based lesson plan." },
            new() { Id = Guid.NewGuid(), CourseId = Course5, ParentElementId = module, SourceElementId = "page-1", Type = "page", Title = "What counts as evidence?" },
            new() { Id = Guid.NewGuid(), CourseId = Course5, ParentElementId = module, SourceElementId = "assignment-1", Type = "assignment", Title = "Design an evidence-informed lesson" } ];
        course.Access.Add(new() { CourseId = Course5, UserId = morgan, GrantedBy = jordan });
        db.AuditLogs.Add(new() { ActorId = jordan, EntityId = Course5, SubjectUserId = morgan, Action = "DemoCourseSharedForReview" });
        db.Courses.Add(course);
    }

    public static readonly Guid SupportResourcesArticleId = Guid.Parse("50000000-0000-0000-0000-000000000002"),
        TablesDraftArticleId = Guid.Parse("50000000-0000-0000-0000-000000000003"),
        AltTextClinicWorkshopId = Guid.Parse("51000000-0000-0000-0000-000000000002"),
        InclusiveAssessmentWorkshopId = Guid.Parse("51000000-0000-0000-0000-000000000003");

    // Two more knowledge articles (one still a draft) and two more workshops (one with a deliberately tiny capacity to
    // demonstrate waitlisting), so the knowledge base and workshop list are not single-item demos.
    private static async Task SeedExtraSupportResourcesAsync(AppDbContext db, Dictionary<string, CurrentUser> people)
    {
        var jordan = people["Jordan Lee"];
        var morgan = people["Morgan Blake"];
        var now = DateTime.UtcNow;
        if (!await db.KnowledgeArticles.AnyAsync(a => a.Id == SupportResourcesArticleId))
        {
            db.KnowledgeArticles.Add(new()
            {
                Id = SupportResourcesArticleId, TenantId = Tenant, Title = "Choosing a support resource that fits the gap", Slug = "choosing-a-support-resource-that-fits-the-gap",
                Body = "Not every finding needs a workshop. A one-line note and a linked job aid resolve most individual accessibility fixes. Reserve a workshop " +
                    "recommendation for patterns that recur across several tickets or courses, where a live session can address the underlying skill gap once instead " +
                    "of repeating the same guidance ticket by ticket.",
                TagsJson = JsonSerializer.Serialize(new[] { "support", "workflow" }), Status = ReviewVocabulary.Published,
                AuthorId = jordan.Id, CreatedAt = now, PublishedAt = now
            });
            db.AuditLogs.Add(new() { ActorId = jordan.Id, EntityId = SupportResourcesArticleId, Action = "KnowledgeArticleCreated" });
        }
        if (!await db.KnowledgeArticles.AnyAsync(a => a.Id == TablesDraftArticleId))
        {
            // Left unpublished on purpose: unlike rubrics and reviews, knowledge articles stay editable, and a draft shows that workflow mid-flight.
            db.KnowledgeArticles.Add(new()
            {
                Id = TablesDraftArticleId, TenantId = Tenant, Title = "Structuring accessible tables (draft)", Slug = "structuring-accessible-tables-draft",
                Body = "Draft notes: every data table needs header cells identifying what each row and column represents. A table used purely for visual layout " +
                    "should not use table markup at all. Still need to add examples of row-header vs. column-header tables before publishing.",
                TagsJson = JsonSerializer.Serialize(new[] { "accessibility", "tables" }), Status = ReviewVocabulary.Draft,
                AuthorId = morgan.Id, CreatedAt = now
            });
            db.AuditLogs.Add(new() { ActorId = morgan.Id, EntityId = TablesDraftArticleId, Action = "KnowledgeArticleCreated" });
        }
        if (!await db.Workshops.AnyAsync(w => w.Id == AltTextClinicWorkshopId))
        {
            var start = now.Date.AddDays(9).AddHours(10);
            // Deliberately tiny capacity so registering a second instructor below demonstrates waitlisting.
            db.Workshops.Add(new()
            {
                Id = AltTextClinicWorkshopId, TenantId = Tenant, Title = "Hands-on alt text clinic", Description = "Bring one page from your own course and leave with corrected, meaningful alt text.",
                StartsAt = start, EndsAt = start.AddHours(1.5), Modality = WorkshopVocabulary.InPerson, Capacity = 1, CreatedBy = morgan.Id
            });
            db.AuditLogs.Add(new() { ActorId = morgan.Id, EntityId = AltTextClinicWorkshopId, Action = "WorkshopCreated" });
        }
        if (!await db.Workshops.AnyAsync(w => w.Id == InclusiveAssessmentWorkshopId))
        {
            var start = now.Date.AddDays(21).AddHours(13);
            db.Workshops.Add(new()
            {
                Id = InclusiveAssessmentWorkshopId, TenantId = Tenant, Title = "Inclusive assessment design", Description = "A hybrid session on redesigning rubrics and instructions around universal design for learning.",
                StartsAt = start, EndsAt = start.AddHours(2), Modality = WorkshopVocabulary.Hybrid, Capacity = null, CreatedBy = morgan.Id
            });
            db.AuditLogs.Add(new() { ActorId = morgan.Id, EntityId = InclusiveAssessmentWorkshopId, Action = "WorkshopCreated" });
        }
        await db.SaveChangesAsync();

        // Real registrations through the service, so capacity and waitlist placement follow the same logic the UI exercises.
        var workshops = new WorkshopService(db);
        var alex = people["Alex Morgan"];
        var priya = people["Priya Chen"];
        await workshops.RegisterAsync(alex, RubricWorkshopId, default);
        await workshops.RegisterAsync(alex, AltTextClinicWorkshopId, default); // fills the clinic's single seat
        await workshops.RegisterAsync(priya, AltTextClinicWorkshopId, default); // lands on the waitlist
        await workshops.RegisterAsync(priya, InclusiveAssessmentWorkshopId, default);
    }

    // Worked examples of reviews and revision tasks across every status in both state machines, so the UI has real data to
    // browse instead of requiring every feature to be walked through by hand first. Jordan Lee's review of WEB 101's
    // assignment is deliberately left unseeded, preserving the README's own "start a review" walkthrough on a clean slate;
    // Morgan Blake's reviews cover the same ground as a pre-populated worked example.
    private static async Task SeedReviewsAndTasksAsync(AppDbContext db, Dictionary<string, CurrentUser> people)
    {
        var reviews = new ReviewService(db);
        var taskService = new TaskService(db);
        var alex = people["Alex Morgan"]; var jordan = people["Jordan Lee"]; var priya = people["Priya Chen"]; var morgan = people["Morgan Blake"];

        async Task<Guid> Elem(Guid courseId, string sourceElementId) =>
            await db.Elements.Where(e => e.CourseId == courseId && e.SourceElementId == sourceElementId).Select(e => e.Id).SingleAsync();
        async Task<ReviewDto?> StartIfNewAsync(CurrentUser reviewer, Guid elementId)
        {
            if (await db.Reviews.AnyAsync(r => r.ElementId == elementId && r.ReviewerId == reviewer.Id)) return null;
            return (await reviews.StartAsync(reviewer, elementId, new StartReviewInput(RubricVersionId), default)).Value;
        }
        // Rates every applicable criterion with a repeating pattern of outcomes: a numeral rates, "NA" marks not applicable,
        // "SKIP" leaves it not reviewed — so published scores show real, varied coverage rather than a uniform perfect score.
        async Task<ReviewDto> RatePatternAsync(CurrentUser reviewer, ReviewDto review, string[] pattern)
        {
            for (var i = 0; i < review.Evaluations.Count; i++)
            {
                var (outcome, rating) = pattern[i % pattern.Length] switch
                {
                    "NA" => (ReviewVocabulary.NotApplicable, (int?)null),
                    "SKIP" => (ReviewVocabulary.NotReviewed, (int?)null),
                    var n => (ReviewVocabulary.Rated, (int?)int.Parse(n))
                };
                review = (await reviews.EvaluateAsync(reviewer, review.Id, review.Evaluations[i].CriterionId,
                    new EvaluationInput(review.Revision, outcome, rating, "", ""), default)).Value!;
            }
            return review;
        }
        async Task<ReviewDto> ConfirmAllProposedAsync(CurrentUser reviewer, ReviewDto review)
        {
            foreach (var finding in review.Findings.Where(f => f.Status == ReviewVocabulary.Proposed).ToList())
                review = (await reviews.UpdateFindingAsync(reviewer, finding.Id, new FindingUpdateInput(review.Revision, ReviewVocabulary.Confirmed, null, null, null, null, null), default)).Value!;
            return review;
        }

        var c1Page1 = await Elem(Course1, "page-1");
        var c1Assignment1 = await Elem(Course1, "assignment-1");
        var c2Page1 = await Elem(Course2, "page-1");
        var c3Page1 = await Elem(Course3, "page-1");
        var c3Page2 = await Elem(Course3, "page-2");
        var c3Assignment1 = await Elem(Course3, "assignment-1");
        var c3Assignment2 = await Elem(Course3, "assignment-2");

        // R1: strong orientation page, two minor automated findings confirmed.
        List<FindingDto> r1Confirmed = [];
        if (await StartIfNewAsync(morgan, c1Page1) is { } r1)
        {
            r1 = await RatePatternAsync(morgan, r1, ["3", "2", "3", "2", "3", "2", "3", "2"]);
            r1 = (await reviews.RunChecksAsync(morgan, r1.Id, r1.Revision, default)).Value!;
            r1 = await ConfirmAllProposedAsync(morgan, r1);
            r1 = (await reviews.PublishAsync(morgan, r1.Id, new PublishReviewInput(r1.Revision, "Strong orientation page; two minor accessibility fixes noted."), default)).Value!;
            r1Confirmed = r1.Findings.Where(f => f.Status == ReviewVocabulary.Confirmed).ToList();
        }
        // R2: clean content, no automated findings — a manual finding demonstrates reviewer-authored findings too.
        // Reviewed by Morgan Blake, not Jordan Lee: DES 120 is deliberately not shared with Jordan Lee (see README), so
        // only the second CTL reviewer, who is shared on both of Alex Morgan's courses, has access to review it.
        List<FindingDto> r2Confirmed = [];
        if (await StartIfNewAsync(morgan, c2Page1) is { } r2)
        {
            r2 = await RatePatternAsync(morgan, r2, ["3", "3", "2", "3", "2", "3", "2", "3"]);
            r2 = (await reviews.AddFindingAsync(morgan, r2.Id, new FindingInput(r2.Revision, null, "Learning goals could name the target audience more specifically",
                "The welcome page states general goals but does not say who the course assumes as a starting point (e.g., prior coding experience).", "Low",
                "Add one sentence naming the assumed starting point, so students can self-assess fit before the add/drop deadline.",
                "Welcome page text: goals are listed without an audience or prerequisite statement."), default)).Value!;
            r2 = (await reviews.PublishAsync(morgan, r2.Id, new PublishReviewInput(r2.Revision, "Clean, well-structured page; one suggestion for audience clarity."), default)).Value!;
            r2Confirmed = r2.Findings.Where(f => f.Status == ReviewVocabulary.Confirmed).ToList();
        }
        // R3: mixed ratings including one not-applicable criterion.
        List<FindingDto> r3Confirmed = [];
        if (await StartIfNewAsync(jordan, c3Page1) is { } r3)
        {
            r3 = await RatePatternAsync(jordan, r3, ["2", "NA", "3", "2", "1", "2", "3", "2"]);
            r3 = (await reviews.RunChecksAsync(jordan, r3.Id, r3.Revision, default)).Value!;
            r3 = await ConfirmAllProposedAsync(jordan, r3);
            r3 = (await reviews.PublishAsync(jordan, r3.Id, new PublishReviewInput(r3.Revision, "Good orientation; one image alt text needs a real description."), default)).Value!;
            r3Confirmed = r3.Findings.Where(f => f.Status == ReviewVocabulary.Confirmed).ToList();
        }
        // R4: a clean assignment with zero findings — not everything needs a fix.
        if (await StartIfNewAsync(jordan, c3Assignment1) is { } r4)
        {
            r4 = await RatePatternAsync(jordan, r4, ["3", "3", "3", "3", "3", "3", "3", "3", "3", "3"]);
            r4 = (await reviews.RunChecksAsync(jordan, r4.Id, r4.Revision, default)).Value!;
            await reviews.PublishAsync(jordan, r4.Id, new PublishReviewInput(r4.Revision, "Well-built assignment: clear deliverables, rubric attached, no accessibility issues found."), default);
        }
        // R5: a review still in progress — partial ratings, automated findings proposed but not yet confirmed, never published.
        if (await StartIfNewAsync(morgan, c3Page2) is { } r5)
        {
            r5 = (await reviews.EvaluateAsync(morgan, r5.Id, r5.Evaluations[0].CriterionId, new EvaluationInput(r5.Revision, ReviewVocabulary.Rated, 1, "Heading levels skip, making the outline hard to follow.", ""), default)).Value!;
            r5 = (await reviews.EvaluateAsync(morgan, r5.Id, r5.Evaluations[1].CriterionId, new EvaluationInput(r5.Revision, ReviewVocabulary.Rated, 2, "", ""), default)).Value!;
            r5 = (await reviews.EvaluateAsync(morgan, r5.Id, r5.Evaluations[2].CriterionId, new EvaluationInput(r5.Revision, ReviewVocabulary.Rated, 1, "", ""), default)).Value!;
            await reviews.RunChecksAsync(morgan, r5.Id, r5.Revision, default);
        }
        // R6: an assignment missing its rubric and table headers; one proposed finding rejected to show that workflow.
        List<FindingDto> r6Confirmed = [];
        if (await StartIfNewAsync(morgan, c3Assignment2) is { } r6)
        {
            r6 = await RatePatternAsync(morgan, r6, ["2", "1", "2", "0", "2", "1", "2", "1", "2", "1"]);
            r6 = (await reviews.RunChecksAsync(morgan, r6.Id, r6.Revision, default)).Value!;
            var proposed = r6.Findings.Where(f => f.Status == ReviewVocabulary.Proposed).ToList();
            foreach (var finding in proposed)
            {
                var status = finding.CheckCode == DeterministicChecks.HeadingEmpty ? ReviewVocabulary.Rejected : ReviewVocabulary.Confirmed;
                r6 = (await reviews.UpdateFindingAsync(morgan, finding.Id,
                    new FindingUpdateInput(r6.Revision, status, null, null, null, null, status == ReviewVocabulary.Rejected ? "The heading is intentionally blank spacing in this template; not worth a task." : null), default)).Value!;
            }
            r6 = (await reviews.PublishAsync(morgan, r6.Id, new PublishReviewInput(r6.Revision, "Reflection prompt is strong, but missing a rubric and table headers."), default)).Value!;
            r6Confirmed = r6.Findings.Where(f => f.Status == ReviewVocabulary.Confirmed).ToList();
        }
        // R7: the README's own walkthrough assignment, reviewed here by the second CTL reviewer as a worked example
        // (Jordan Lee's own review of this same assignment is intentionally left for the user to create by hand).
        List<FindingDto> r7Confirmed = [];
        if (await StartIfNewAsync(morgan, c1Assignment1) is { } r7)
        {
            r7 = await RatePatternAsync(morgan, r7, ["2", "2", "2", "1", "1", "2", "2", "2", "2", "2"]);
            r7 = (await reviews.RunChecksAsync(morgan, r7.Id, r7.Revision, default)).Value!;
            r7 = await ConfirmAllProposedAsync(morgan, r7);
            r7 = (await reviews.PublishAsync(morgan, r7.Id, new PublishReviewInput(r7.Revision, "Good first draft; needs alt text, a rubric and table headers before it is ready."), default)).Value!;
            r7Confirmed = r7.Findings.Where(f => f.Status == ReviewVocabulary.Confirmed).ToList();
        }

        // Tasks pick their finding by check code (null for a reviewer-authored finding), not list position, so each task's
        // title always matches the finding it is built from.
        static Guid? Confirmed(List<FindingDto> confirmed, string? checkCode) =>
            confirmed.Where(f => f.CheckCode == checkCode).Select(f => (Guid?)f.Id).FirstOrDefault();
        async Task<TaskDto?> CreateTaskIfNewAsync(CurrentUser creator, Guid courseId, string title, string instructions, string priority, Guid? findingId, DateTime? dueAt = null)
        {
            if (findingId is not { } id || await db.RevisionTasks.AnyAsync(t => t.Title == title)) return null;
            return (await taskService.CreateAsync(creator, new CreateTaskInput(courseId, title, instructions, priority, dueAt, [id]), default)).Value;
        }
        async Task<TaskDto> Transition(CurrentUser actor, TaskDto task, string toStatus, string? reason = null) =>
            (await taskService.TransitionAsync(actor, task.Id, new TaskTransitionInput(task.Revision, toStatus, reason), default)).Value!;
        async Task<TaskDto> Comment(CurrentUser actor, TaskDto task, string body) => (await taskService.CommentAsync(actor, task.Id, new TaskCommentInput(body), default)).Value!;

        // Task 1: the full happy path, including a simulated instructor edit, resolved end to end.
        if (await CreateTaskIfNewAsync(morgan, Course1, "Add missing alt text on the first accessible page assignment",
            "The automated check found an image with no text alternative. Add a short description of what the layout screenshot shows.", "High", Confirmed(r7Confirmed, DeterministicChecks.ImageAltMissing)) is { } t1)
        {
            t1 = await Transition(morgan, t1, TaskVocabulary.NeedsRevision);
            t1 = await Transition(alex, t1, TaskVocabulary.InProgress);
            t1 = await Comment(alex, t1, "Added a description of the layout screenshot — let me know if it needs more detail.");
            t1 = (await taskService.SimulateUpdateAsync(alex, t1.Id, new SimulateUpdateInput(t1.Revision,
                "Create a page with semantic headings, descriptive links, and useful image alternatives. The sample layout screenshot now reads: " +
                "\"Screenshot of a two-column page layout with a sidebar navigation and a main content area.\" Submit a short reflection. No student submissions are stored in TeachWell."), default)).Value!;
            t1 = await Transition(alex, t1, TaskVocabulary.ReadyForReview);
            t1 = await Comment(morgan, t1, "Looks good — the alt text now describes the layout. Resolving.");
            await Transition(morgan, t1, TaskVocabulary.Resolved);
        }
        // Task 2: currently being worked on.
        if (await CreateTaskIfNewAsync(jordan, Course3, "Add header cells to the data ethics reflection table",
            "The table comparing criteria to notes has no header row. Mark the header cells so screen readers announce column meaning.", "Medium", Confirmed(r6Confirmed, DeterministicChecks.TableHeaders), DateTime.UtcNow.AddDays(10)) is { } t2)
        {
            t2 = await Transition(jordan, t2, TaskVocabulary.NeedsRevision);
            t2 = await Transition(priya, t2, TaskVocabulary.InProgress);
            await Comment(priya, t2, "On it — will mark the first row as header cells this week.");
        }
        // Task 3: sent back for changes, then resumed — shows the full ChangesRequested/resubmit cycle.
        if (await CreateTaskIfNewAsync(morgan, Course3, "Attach a rubric to the data ethics reflection",
            "No rubric or criteria are attached to this assignment. Attach one so students know how the reflection is evaluated.", "Medium", Confirmed(r6Confirmed, DeterministicChecks.RubricMissing)) is { } t3)
        {
            t3 = await Transition(morgan, t3, TaskVocabulary.NeedsRevision);
            t3 = await Transition(priya, t3, TaskVocabulary.InProgress);
            t3 = await Transition(priya, t3, TaskVocabulary.ReadyForReview);
            t3 = await Transition(morgan, t3, TaskVocabulary.ChangesRequested, "The attached rubric only has point values, not descriptive performance levels. Please add short descriptions for each level.");
            await Transition(priya, t3, TaskVocabulary.InProgress);
        }
        // Task 4: blocked on something outside the instructor's control.
        if (await CreateTaskIfNewAsync(jordan, Course3, "Replace the chart intro image's file-name alt text",
            "The intro image's alt text is just its file name. Replace it with a real description once a licensed replacement image is available.", "Low", Confirmed(r3Confirmed, DeterministicChecks.ImageAltFilename)) is { } t4)
        {
            t4 = await Transition(jordan, t4, TaskVocabulary.NeedsRevision);
            await Transition(jordan, t4, TaskVocabulary.Blocked, "Waiting on a replacement image from the library's open media collection.");
        }
        // Task 5: marked not applicable — the underlying issue was already addressed elsewhere.
        if (await CreateTaskIfNewAsync(morgan, Course2, "Name the assumed starting point on the welcome page",
            "Add a sentence naming the assumed prior experience so students can self-assess fit.", "Low", Confirmed(r2Confirmed, null)) is { } t5)
        {
            t5 = await Transition(morgan, t5, TaskVocabulary.NeedsRevision);
            await Transition(morgan, t5, TaskVocabulary.NotApplicable, "Learning goals were rewritten institution-wide this term; the rewritten page already states the assumed starting point.");
        }
        // R8 + Task 7: Morgan Blake reviews Jordan Lee's own course, so Jordan's Instructor view has published feedback and assigned work.
        var c5Page1 = await Elem(Course5, "page-1");
        var c5Assignment1 = await Elem(Course5, "assignment-1");
        List<FindingDto> r8Confirmed = [];
        if (await StartIfNewAsync(morgan, c5Assignment1) is { } r8)
        {
            r8 = await RatePatternAsync(morgan, r8, ["2", "2", "1", "0", "2", "3", "2", "1", "2", "2"]);
            r8 = (await reviews.RunChecksAsync(morgan, r8.Id, r8.Revision, default)).Value!;
            r8 = await ConfirmAllProposedAsync(morgan, r8);
            r8 = (await reviews.PublishAsync(morgan, r8.Id, new PublishReviewInput(r8.Revision, "Clear task; attach a rubric and mark the table header row."), default)).Value!;
            r8Confirmed = r8.Findings.Where(f => f.Status == ReviewVocabulary.Confirmed).ToList();
        }
        if (await StartIfNewAsync(morgan, c5Page1) is { } r9)
        {
            r9 = await RatePatternAsync(morgan, r9, ["3", "2", "2", "3", "2", "1", "3", "2"]);
            r9 = (await reviews.RunChecksAsync(morgan, r9.Id, r9.Revision, default)).Value!;
            r9 = await ConfirmAllProposedAsync(morgan, r9);
            await reviews.PublishAsync(morgan, r9.Id, new PublishReviewInput(r9.Revision, "Good framing; fix the file-name alt text and the vague reading-list link."), default);
        }
        if (await CreateTaskIfNewAsync(morgan, Course5, "Attach a rubric to the evidence-informed lesson",
            "No rubric is attached. Add one so students know how the lesson plan and rationale are evaluated.", "High", Confirmed(r8Confirmed, DeterministicChecks.RubricMissing), DateTime.UtcNow.AddDays(7)) is { } t7)
        {
            t7 = await Transition(morgan, t7, TaskVocabulary.NeedsRevision);
            t7 = await Transition(jordan, t7, TaskVocabulary.InProgress);
            await Comment(jordan, t7, "Drafting a four-level rubric now.");
        }
        if (await CreateTaskIfNewAsync(morgan, Course5, "Mark header cells in the lesson grading table",
            "The grading table has no header row. Mark the first row as header cells.", "Low", Confirmed(r8Confirmed, DeterministicChecks.TableHeaders)) is { } t8)
            await Transition(morgan, t8, TaskVocabulary.NeedsRevision);

        // Task 6: left in Draft — visible only to CTL until it is assigned, demonstrating that visibility rule.
        if (Confirmed(r1Confirmed, DeterministicChecks.HeadingEmpty) is { } emptyHeading)
            await CreateTaskIfNewAsync(morgan, Course1, "Remove the empty heading on the welcome page",
                "There is an empty heading with no text used purely for spacing. Remove it rather than using headings for layout.", "Low", emptyHeading);
    }

    // Support tickets across every status in the triage state machine, with and without course links, assignment and recommendations.
    private static async Task SeedSupportTicketsAsync(AppDbContext db, Dictionary<string, CurrentUser> people)
    {
        var support = new SupportService(db);
        var alex = people["Alex Morgan"]; var jordan = people["Jordan Lee"]; var priya = people["Priya Chen"]; var morgan = people["Morgan Blake"];

        async Task<SupportTicketDto?> CreateIfNewAsync(CurrentUser requester, string title, string description, string category, string priority,
            Guid? courseId = null, Guid? elementId = null, Guid? revisionTaskId = null)
        {
            if (await db.SupportTickets.AnyAsync(t => t.Title == title)) return null;
            return (await support.CreateAsync(requester, new CreateTicketInput(courseId, elementId, revisionTaskId, category, title, description, priority), default)).Value;
        }
        async Task<SupportTicketDto> Transition(CurrentUser actor, SupportTicketDto ticket, string toStatus, string? reason = null) =>
            (await support.TransitionAsync(actor, ticket.Id, new TicketTransitionInput(ticket.Revision, toStatus, reason), default)).Value!;
        async Task<SupportTicketDto> Comment(CurrentUser actor, SupportTicketDto ticket, string body) => (await support.CommentAsync(actor, ticket.Id, new TicketCommentInput(body), default)).Value!;
        async Task<SupportTicketDto> Assign(CurrentUser actor, SupportTicketDto ticket, Guid? assigneeId) => (await support.AssignAsync(actor, ticket.Id, new TicketAssignInput(ticket.Revision, assigneeId), default)).Value!;
        async Task<SupportTicketDto> Recommend(CurrentUser actor, SupportTicketDto ticket, Guid? articleId, Guid? workshopId) =>
            (await support.RecommendAsync(actor, ticket.Id, new TicketRecommendInput(ticket.Revision, articleId, workshopId), default)).Value!;

        // TK1: new, unassigned, sitting in the shared triage queue.
        await CreateIfNewAsync(alex, "Need guidance on alt text for charts", "I have several charts in my course and I'm not sure how to describe them well for screen reader users.", "Accessibility", "Medium");

        // TK2: triaged and assigned, not yet started.
        if (await CreateIfNewAsync(priya, "Can someone look at my course's competency alignment?", "I've mapped some assignments to competencies but I'm not sure I'm using the feature correctly.",
            "CourseDesign", "Low", courseId: Course3) is { } tk2)
        {
            tk2 = await Transition(morgan, tk2, SupportVocabulary.Triaged);
            await Assign(morgan, tk2, morgan.Id);
        }

        // TK3: in progress, linked to a course, element and the resolved revision task above, with a recommended article.
        var t1Id = await db.RevisionTasks.Where(t => t.Title == "Add missing alt text on the first accessible page assignment").Select(t => (Guid?)t.Id).SingleOrDefaultAsync();
        if (await CreateIfNewAsync(alex, "Follow-up on the alt text revision task", "The CTL review flagged a missing alt text issue on my assignment page. What's a good description pattern to use going forward?",
            "Accessibility", "Medium", revisionTaskId: t1Id) is { } tk3)
        {
            tk3 = await Transition(jordan, tk3, SupportVocabulary.InProgress);
            tk3 = await Assign(jordan, tk3, jordan.Id);
            tk3 = await Comment(alex, tk3, "For context, this is my first time writing alt text for a UI screenshot rather than a photo.");
            tk3 = await Comment(jordan, tk3, "For screenshots, describe the layout and purpose, not every visual detail — see the linked article.");
            await Recommend(jordan, tk3, AltTextArticleId, null);
        }

        // TK4: waiting on the instructor, with a recommended workshop, linked to the unshared course.
        if (await CreateIfNewAsync(priya, "Would a workshop help with my assessment redesign?", "I'm reworking several rubrics and would rather learn the pattern once than fix each rubric ad hoc.",
            "CourseDesign", "Medium", courseId: Course4) is { } tk4)
        {
            tk4 = await Transition(morgan, tk4, SupportVocabulary.InProgress);
            tk4 = await Assign(morgan, tk4, morgan.Id);
            tk4 = await Recommend(morgan, tk4, null, InclusiveAssessmentWorkshopId);
            await Transition(morgan, tk4, SupportVocabulary.WaitingOnInstructor, "Recommended the inclusive assessment design workshop; checking back after the session.");
        }

        // TK5: resolved.
        if (await CreateIfNewAsync(alex, "Table headers keep disappearing after I copy from Word", "Every time I paste a table from Word into a Canvas page, the header formatting doesn't carry over.",
            "CourseDesign", "Medium") is { } tk5)
        {
            tk5 = await Transition(jordan, tk5, SupportVocabulary.InProgress);
            tk5 = await Assign(jordan, tk5, jordan.Id);
            await Transition(jordan, tk5, SupportVocabulary.Resolved, "Pasting as plain text and rebuilding the table in Canvas's table tool keeps the header row intact.");
        }

        // TK6: resolved and closed, linked to a course.
        if (await CreateIfNewAsync(priya, "Captions missing on an embedded video", "A video embedded in one of my pages doesn't have captions and I'm not sure how to add them.",
            "Multimedia", "High", courseId: Course3) is { } tk6)
        {
            tk6 = await Transition(morgan, tk6, SupportVocabulary.InProgress);
            tk6 = await Assign(morgan, tk6, morgan.Id);
            tk6 = await Transition(morgan, tk6, SupportVocabulary.Resolved, "Walked through the captioning tool together on a call.");
            await Transition(morgan, tk6, SupportVocabulary.Closed);
        }

        // TK7: closed, then reopened — demonstrates that a ticket can return to active work from either terminal-looking state.
        if (await CreateIfNewAsync(alex, "Question about the review scoring calculation", "I want to understand how the percentage score is calculated before I share it with my department.",
            "Other", "Low") is { } tk7)
        {
            tk7 = await Transition(jordan, tk7, SupportVocabulary.InProgress);
            tk7 = await Assign(jordan, tk7, jordan.Id);
            tk7 = await Transition(jordan, tk7, SupportVocabulary.Resolved, "Not-applicable criteria are excluded from the denominator; not-reviewed criteria are reported separately, never scored as zero.");
            tk7 = await Transition(jordan, tk7, SupportVocabulary.Closed);
            await Transition(jordan, tk7, SupportVocabulary.InProgress, "Reopening — new guidance on required-criteria coverage was published and the original answer needs an update.");
        }
    }

    // Course-design competencies mapped to assessment evidence, with a deliberate orphan competency and an unmapped
    // assignment on Course3 so the alignment report has real gaps to surface, and a fully aligned course for contrast.
    private static async Task SeedCompetenciesAsync(AppDbContext db, Dictionary<string, CurrentUser> people)
    {
        var competencies = new CompetencyService(db);
        var alex = people["Alex Morgan"]; var priya = people["Priya Chen"];

        async Task<Guid> Elem(Guid courseId, string sourceElementId) =>
            await db.Elements.Where(e => e.CourseId == courseId && e.SourceElementId == sourceElementId).Select(e => e.Id).SingleAsync();

        if (!await db.CourseCompetencies.AnyAsync(c => c.CourseId == Course1 && c.Code == "COMP-A11Y"))
        {
            var created = await competencies.CreateAsync(alex, Course1, new CreateCompetencyInput("COMP-A11Y", "Produce accessible web content",
                "Students create web pages that meet baseline accessibility expectations: semantic headings, descriptive links and meaningful image alternatives."), default);
            if (created.Value is { } comp) await competencies.MapAsync(alex, Course1, comp.Id, await Elem(Course1, "assignment-1"), default);
        }
        if (!await db.CourseCompetencies.AnyAsync(c => c.CourseId == Course3 && c.Code == "COMP-DATA"))
        {
            var created = await competencies.CreateAsync(priya, Course3, new CreateCompetencyInput("COMP-DATA", "Interpret and communicate data visually",
                "Students build a chart from a real dataset and explain, in their own words, what it shows."), default);
            if (created.Value is { } comp) await competencies.MapAsync(priya, Course3, comp.Id, await Elem(Course3, "assignment-1"), default);
        }
        // Deliberately left unmapped: no assignment currently demonstrates this competency, so the alignment report should flag it as an orphan.
        if (!await db.CourseCompetencies.AnyAsync(c => c.CourseId == Course3 && c.Code == "COMP-ETHICS"))
            await competencies.CreateAsync(priya, Course3, new CreateCompetencyInput("COMP-ETHICS", "Reason about the ethics of data use",
                "Students identify who collected a dataset, who benefits from its use, and who could be harmed by it."), default);
        // assignment-2 ("Data ethics reflection") is intentionally left unmapped to any competency, so the alignment report also has an unmapped assignment to show.
    }

    public static readonly Guid CollegeStandardSetId = Guid.Parse("60000000-0000-0000-0000-000000000001"), CollegeStandardVersionId = Guid.Parse("61000000-0000-0000-0000-000000000001"),
        OnlineStandardSetId = Guid.Parse("60000000-0000-0000-0000-000000000002"), OnlineStandardVersionId = Guid.Parse("61000000-0000-0000-0000-000000000002");

    // The college-wide standard set, equal to TeachWell's built-in defaults, so reviews behave exactly as before until the CTL changes it.
    private static async Task SeedStandardSetAsync(AppDbContext db, Guid author)
    {
        if (await db.StandardSets.AnyAsync(s => s.Id == CollegeStandardSetId)) return;
        await PublishStandardSetAsync(db, author, CollegeStandardSetId, CollegeStandardVersionId, "College-wide course standards (demo)", null, null,
            StandardSetContent.InitialDefaults(), "Starting point: TeachWell's built-in check settings and guidance.");
    }

    // An Online-mode set showing what the CTL can change, plus the course classification (D4) that selects it.
    private static async Task SeedOnlineStandardsAsync(AppDbContext db, Guid author)
    {
        if (await db.StandardSets.AnyAsync(s => s.Id == OnlineStandardSetId)) return;
        // WithNewChecks brings in the checks added since the college-wide v1 was published, including the media checks.
        // That keeps the demo honest about ADR 011: a new check reaches instructors only through a published version,
        // so the college-wide fallback still runs the original set and this Online set runs the newer one.
        var checks = StandardSetContent.WithNewChecks(StandardSetContent.InitialDefaults()).Select(c => c.Code switch
        {
            DeterministicChecks.ImageAltMissing or DeterministicChecks.LinkEmpty or DeterministicChecks.ItemUnavailable => c with { Required = true },
            CourseChecks.StartMissing => c with
            {
                Required = true,
                Guidance = c.Guidance with { Why = "In an online course the Start here module is the first class meeting. Without it, students have no one to ask where to begin, and early confusion is the most common reason online students fall behind. (UDL: Engagement.)" }
            },
            CourseChecks.ModuleUnpublished => c with { Severity = "Medium" },
            DeterministicChecks.LinkVague => c with { Lists = new Dictionary<string, IReadOnlyList<string>> { [DeterministicChecks.VaguePhrases] = [.. c.Lists[DeterministicChecks.VaguePhrases], "watch this", "video", "slides"] } },
            _ => c
        }).ToList();
        await PublishStandardSetAsync(db, author, OnlineStandardSetId, OnlineStandardVersionId, "Online course standards (demo)", StandardSetVocabulary.Online, null, checks,
            "Online courses: accessibility basics and a clear starting point are required; video and slide links must be descriptive; video and audio are checked for captions.");
        foreach (var (id, mode, department) in new[] { (Course1, StandardSetVocabulary.Online, "Computing"), (Course2, StandardSetVocabulary.FaceToFace, "Design"),
            (Course3, StandardSetVocabulary.Hybrid, "Computing"), (Course5, StandardSetVocabulary.Online, "Education") })
            if (await db.Courses.SingleOrDefaultAsync(c => c.Id == id && c.DeliveryMode == null && c.Department == null) is { } course)
            {
                course.DeliveryMode = mode; course.Department = department; course.Revision = Guid.NewGuid();
                db.AuditLogs.Add(new() { ActorId = author, EntityId = id, Action = "DemoCourseClassified" });
            }
        await db.SaveChangesAsync();
    }

    public static readonly Guid CollegeStandardV2Id = Guid.Parse("61000000-0000-0000-0000-000000000003"), OnlineStandardV2Id = Guid.Parse("61000000-0000-0000-0000-000000000004");

    // Version 2 of each demo set adopts the checks added in ADR 011 slice 3; the Online set also tightens two of them.
    private static async Task SeedStandardsUpgradeAsync(AppDbContext db, Guid author)
    {
        await UpgradeAsync(CollegeStandardSetId, CollegeStandardV2Id, "Adds content, pacing and outcome checks (reading level, page length, workload, due-date clustering, outcome verbs).", c => c);
        await UpgradeAsync(OnlineStandardSetId, OnlineStandardV2Id, "Adds the new checks; online modules must open with an overview and hold at most 12 items.", c => c.Code switch
        {
            CourseDesignChecks.ModuleNoOverview => c with { Required = true, Severity = "Medium" },
            CourseDesignChecks.ModuleTooManyItems => c with { Numbers = new Dictionary<string, decimal> { [CourseDesignChecks.MaxItems] = 12 } },
            _ => c
        });

        async Task UpgradeAsync(Guid setId, Guid versionId, string notes, Func<StandardCheckSetting, StandardCheckSetting> change)
        {
            if (await db.StandardSetVersions.AnyAsync(v => v.Id == versionId) || !await db.StandardSets.AnyAsync(s => s.Id == setId)) return;
            var latest = await db.StandardSetVersions.Where(v => v.SetId == setId).OrderByDescending(v => v.VersionNumber).FirstAsync();
            // A CTL draft in progress, or a later version, is left alone.
            if (latest.Status != ReviewVocabulary.Published || latest.VersionNumber != 1) return;
            var checks = StandardSetContent.WithNewChecks(StandardSetContent.Parse(latest.ChecksJson)).Select(change);
            var version = new StandardSetVersion { Id = versionId, SetId = setId, VersionNumber = 2, CreatedBy = author, Notes = notes, ChecksJson = StandardSetContent.Serialize(checks) };
            db.StandardSetVersions.Add(version);
            await db.SaveChangesAsync();
            await PublishVersionAsync(db, author, version);
        }
    }

    private static async Task PublishVersionAsync(AppDbContext db, Guid author, StandardSetVersion version)
    {
        version.Status = ReviewVocabulary.Published; version.PublishedBy = author; version.PublishedAt = DateTime.UtcNow; version.Revision = Guid.NewGuid();
        db.AuditLogs.Add(new() { ActorId = author, EntityId = version.Id, Action = "DemoStandardSetPublished" });
        await db.SaveChangesAsync();
    }

    private static async Task PublishStandardSetAsync(AppDbContext db, Guid author, Guid setId, Guid versionId, string name, string? mode, string? department,
        IEnumerable<StandardCheckSetting> checks, string notes)
    {
        // Insert as a draft, then publish, matching how the rubric seed respects the database immutability trigger.
        var version = new StandardSetVersion { Id = versionId, SetId = setId, VersionNumber = 1, CreatedBy = author, Notes = notes, ChecksJson = StandardSetContent.Serialize(checks) };
        db.StandardSets.Add(new StandardSet { Id = setId, TenantId = Tenant, Name = name, DeliveryMode = mode, Department = department,
            ScopeKey = StandardSetVocabulary.ScopeKey(mode, department), CreatedBy = author, Versions = [version] });
        await db.SaveChangesAsync();
        await PublishVersionAsync(db, author, version);
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded) throw new InvalidOperationException("Demo seed failed: " + string.Join(", ", result.Errors.Select(e => e.Code)));
    }
}
