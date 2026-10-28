using TeachWell.Domain;
using TeachWell.Infrastructure.Canvas;
using Xunit;

// Media capture and the captions checks (ROADMAP item 5a, ADR 018 amendment).
public class MediaCheckUnitTests
{
    // Only the media checks, so unrelated checks do not add findings to these scenarios.
    private static CheckProfile Only(params string[] codes)
    {
        var all = CheckRegistry.All.ToDictionary(c => c.Code, c => c.Defaults() with { Enabled = codes.Contains(c.Code) });
        return new CheckProfile(all);
    }
    private static readonly CheckProfile MediaOnly = Only(MediaChecks.CaptionsMissing, MediaChecks.CaptionsUnconfirmed);
    private static ContentEvidence WithMedia(params MediaEvidence[] media) => new(ContentEvidence.CurrentVersion, [], [], [], [], null, false, media);
    private static CheckReport Run(ContentEvidence evidence) => DeterministicChecks.Run("page", "Published", evidence, MediaOnly, "");

    [Fact] public void Media_is_captured_from_markup_that_is_then_stripped_and_never_rendered()
    {
        var (text, evidence) = CanvasText.Normalize(
            "<h2>Week 3</h2>" +
            "<video controls><source src='/files/1/lecture.mp4'><track kind='captions' src='/files/2/c.vtt'></video>" +
            "<video controls title='Lab walkthrough'><source src='/files/3/lab.mp4'></video>" +
            "<audio controls src='/files/4/podcast.mp3'></audio>" +
            "<iframe title='Unit 3 lecture' src='https://www.youtube.com/embed/abc123?secrettoken=xyz'></iframe>" +
            "<a class='instructure_inline_media_comment' href='/media_objects/m-99'>recorded feedback</a>",
            new Uri("https://canvas.example.test/"));

        var media = Assert.IsAssignableFrom<IReadOnlyList<MediaEvidence>>(evidence.Media);
        Assert.Equal(5, media.Count);
        // Kinds, and whether the page can settle captions: a track is authoritative, an embed is not.
        Assert.Equal([MediaKinds.Video, MediaKinds.Video, MediaKinds.Audio, MediaKinds.Embed, MediaKinds.Embed], media.Select(m => m.Kind));
        Assert.Equal([true, false, false, null, null], media.Select(m => m.HasCaptionTrack));
        Assert.Equal("Lab walkthrough", media[1].Title);
        Assert.Equal("Unit 3 lecture", media[3].Title);
        Assert.Equal("www.youtube.com", media[3].Provider);
        Assert.Null(media[2].Provider); // same-origin Canvas file: no host is kept

        // The security posture is unchanged: the iframe is still stripped, and no URL or token is persisted.
        Assert.DoesNotContain("iframe", text);
        Assert.DoesNotContain("youtube", text);
        Assert.DoesNotContain("secrettoken", evidence.Serialize());
        Assert.DoesNotContain("lecture.mp4", evidence.Serialize());
        Assert.Null(CanvasText.Preview("<iframe src='https://www.youtube.com/embed/abc123'></iframe>"));
    }

    [Fact] public void An_empty_page_captures_an_empty_media_list_rather_than_none()
    {
        // Distinguishing "no media" from "media not captured" is the whole point of the nullable field.
        Assert.Empty(CanvasText.Normalize("", null).Evidence.Media!);
        Assert.Empty(CanvasText.Normalize("<p>Just text.</p>", null).Evidence.Media!);
        Assert.Empty(Run(WithMedia()).Findings);
    }

    [Fact] public void A_player_with_no_captions_track_is_a_definite_finding()
    {
        var report = Run(WithMedia(new MediaEvidence(0, MediaKinds.Video, null, "Lab walkthrough", false)));
        var finding = Assert.Single(report.Findings);
        Assert.Equal(MediaChecks.CaptionsMissing, finding.CheckCode);
        Assert.Equal("High", finding.Severity);
        Assert.Contains("\"Lab walkthrough\"", finding.Description);
        Assert.Contains("no <track kind=\"captions\">", finding.Evidence);
        Assert.Contains("captions track", finding.Recommendation);

        // Audio is advised to add a transcript rather than a captions track on a video.
        var audio = Assert.Single(Run(WithMedia(new MediaEvidence(0, MediaKinds.Audio, null, null, false))).Findings);
        Assert.Contains("transcript", audio.Recommendation);
        Assert.Contains("audio 1", audio.Description); // no accessible name: described by position

        // A captioned player is not flagged by either check.
        Assert.Empty(Run(WithMedia(new MediaEvidence(0, MediaKinds.Video, null, "Captioned", true))).Findings);
    }

    [Fact] public void An_embed_asks_the_instructor_to_confirm_rather_than_asserting_what_it_cannot_see()
    {
        var finding = Assert.Single(Run(WithMedia(new MediaEvidence(0, MediaKinds.Embed, "www.youtube.com", "Unit 3 lecture", null))).Findings);
        Assert.Equal(MediaChecks.CaptionsUnconfirmed, finding.CheckCode);
        // Lower than a definite finding, because the media may already be captioned.
        Assert.Equal("Medium", finding.Severity);
        Assert.Contains("cannot see whether", finding.Description);
        Assert.Contains("hosted at www.youtube.com", finding.Description);
        Assert.Contains("cannot be determined from the page", finding.Evidence);
        // It never claims captions are missing, and never guesses from the host.
        Assert.DoesNotContain("has no captions", finding.Description);
    }

    [Fact] public void Snapshots_taken_before_media_capture_report_a_limitation_instead_of_passing()
    {
        // Version 1 evidence: Media is null, not empty.
        var old = new ContentEvidence(1, [], [], [], []);
        Assert.Null(old.Media);
        var report = Run(old);
        Assert.Empty(report.Findings);
        Assert.Contains(report.Limitations, l => l.Contains("predates media capture") && l.Contains("Re-import"));
        // Round-tripping version 1 JSON keeps Media null rather than defaulting it to an empty list.
        Assert.Null(ContentEvidence.Parse(old.Serialize())!.Media);
    }

    [Fact] public void Both_checks_relate_to_the_captions_criterion_and_the_guide_rates_them_as_slow_work()
    {
        Assert.Equal([Wcag.Captions], CheckRegistry.Find(MediaChecks.CaptionsMissing)!.WcagCriteria);
        Assert.Equal([Wcag.Captions], CheckRegistry.Find(MediaChecks.CaptionsUnconfirmed)!.WcagCriteria);
        Assert.Equal([MediaChecks.CaptionsMissing, MediaChecks.CaptionsUnconfirmed], Wcag.Checks(Wcag.Captions));
        // Captioning is slow, so it must not be ranked as a quick win.
        Assert.True(GuideCatalog.MinutesFor(MediaChecks.CaptionsMissing) >= 30);

        // A captions strength is claimed only where the course has media.
        var none = new GuideCoverage(Pages: 3, Assessments: 1, Images: 0, Links: 2, Headings: 4, Tables: 0, Modules: 2, Outcomes: 1, Items: 3, Media: 0);
        Assert.DoesNotContain(MediaChecks.CaptionsMissing, ImprovementGuide.Strengths([MediaChecks.CaptionsMissing], [], none).Select(s => s.CheckCode));
        Assert.Contains(MediaChecks.CaptionsMissing, ImprovementGuide.Strengths([MediaChecks.CaptionsMissing], [], none with { Media = 2 }).Select(s => s.CheckCode));
        // "Nothing to confirm" is never a strength: a confirmed step is the instructor's word, not a check's.
        Assert.Empty(ImprovementGuide.Strengths([MediaChecks.CaptionsUnconfirmed], [], none with { Media = 2 }));
    }

    [Fact] public void New_checks_do_not_run_under_standard_sets_published_before_them()
    {
        // ADR 011: a check a published version does not mention does not run under it.
        Assert.DoesNotContain(MediaChecks.CaptionsMissing, CheckRegistry.InitialCodes);
        Assert.DoesNotContain(MediaChecks.CaptionsUnconfirmed, CheckRegistry.InitialCodes);
        // ToProfile is the production path: it disables every check the published version does not mention. Building a
        // CheckProfile directly would not, because an unmentioned check falls back to its own (enabled) defaults.
        var initial = StandardSetContent.ToProfile(StandardSetContent.InitialDefaults());
        var findings = DeterministicChecks.Run("page", "Published", WithMedia(new MediaEvidence(0, MediaKinds.Video, null, null, false)), initial, "").Findings;
        Assert.DoesNotContain(findings, f => f.CheckCode.StartsWith("MEDIA_"));

        // A new draft of that version picks the media checks up at their defaults, so publishing is how they arrive.
        var next = StandardSetContent.WithNewChecks(StandardSetContent.InitialDefaults());
        Assert.Contains(next, c => c.Code == MediaChecks.CaptionsMissing && c.Enabled);
    }
}

// Added after learning the college delivers syllabi through Simple Syllabus, an LTI tool (ADR 008).
public class EmbeddedToolUnitTests
{
    private static readonly CheckProfile MediaOnly = new(CheckRegistry.All.ToDictionary(
        c => c.Code, c => c.Defaults() with { Enabled = c.Code.StartsWith("MEDIA_") }));

    private static readonly Uri Origin = new("https://canvas.example.test/");
    private static CheckReport Run(string html, string text = "x") =>
        DeterministicChecks.Run("page", "Published", CanvasText.Normalize(html, Origin).Evidence, MediaOnly, text);

    [Fact] public void An_embedded_lti_tool_is_reported_as_not_inspected_rather_than_asked_about_captions()
    {
        // A Simple Syllabus launch in a Canvas page body, as this college actually has it (ADR 008).
        var report = Run("<p>Your syllabus:</p><iframe title='Simple Syllabus' src='https://canvas.example.test/courses/1/external_tools/42'></iframe>", "Your syllabus:");
        Assert.DoesNotContain(report.Findings, f => f.CheckCode == MediaChecks.CaptionsUnconfirmed);
        // Not silently dropped either: the reviewer is told something on the page was not inspected.
        Assert.Contains(report.Limitations, l => l.Contains("1 embedded tool was not inspected"));
    }

    [Fact] public void Recognised_players_are_still_asked_about_captions_and_tools_are_counted_separately()
    {
        var report = Run(
            "<iframe title='Lecture' src='https://www.youtube.com/embed/abc'></iframe>" +
            "<iframe title='Studio recording' src='https://canvas.example.test/media_attachments_iframe/99'></iframe>" +
            "<iframe title='Padlet' src='https://padlet.com/embed/xyz'></iframe>" +
            "<iframe title='Sign-up' src='https://docs.google.com/forms/d/e/x/viewform'></iframe>");
        // The two players are asked about; the two tools are not.
        Assert.Equal(2, report.Findings.Count(f => f.CheckCode == MediaChecks.CaptionsUnconfirmed));
        Assert.Contains(report.Limitations, l => l.Contains("2 embedded tools were not inspected"));
    }

    [Fact] public void Host_matching_follows_domain_labels_so_a_lookalike_domain_is_not_trusted()
    {
        IReadOnlyList<MediaEvidence> Media(string src) =>
            CanvasText.Normalize($"<iframe src='{src}'></iframe>", Origin).Evidence.Media!;

        // A tenant or regional subdomain of a real provider is a player.
        Assert.Equal(MediaKinds.Embed, Media("https://college.hosted.panopto.com/Panopto/Pages/Embed.aspx?id=1")[0].Kind);
        Assert.Equal(MediaKinds.Embed, Media("https://eu.kaltura.com/p/1/embedIframeJs")[0].Kind);
        // A lookalike registered elsewhere is not. Substring matching would have got this wrong.
        Assert.Equal(MediaKinds.Tool, Media("https://youtube.com.evil.test/embed/abc")[0].Kind);
        Assert.Equal(MediaKinds.Tool, Media("https://notyoutube.com/embed/abc")[0].Kind);
        // A protocol-relative URL still resolves to its host.
        Assert.Equal(MediaKinds.Embed, Media("//player.vimeo.com/video/123")[0].Kind);
    }

    [Fact] public void An_embedded_tool_is_not_evidence_about_captions_either_way()
    {
        // Were a Tool counted as media, a course whose only "media" is an LTI launch would be credited with the
        // strength "every video and audio player carries captions" on the basis of no media at all.
        Assert.False(MediaKinds.IsPlayer(MediaKinds.Tool));
        Assert.All(new[] { MediaKinds.Video, MediaKinds.Audio, MediaKinds.Embed }, k => Assert.True(MediaKinds.IsPlayer(k)));
    }
}
