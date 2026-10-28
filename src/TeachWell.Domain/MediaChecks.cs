namespace TeachWell.Domain;

// Captions on video and audio (ROADMAP item 5a). These read the version 2 media evidence; a snapshot captured before
// that reports a limitation rather than passing.
//
// The two checks differ in what the page can prove, and the project's rule is that a finding states only what the
// evidence supports (ADR 005):
//
// - An HTML <video> or <audio> element either carries a captions track or it does not, so MEDIA_CAPTIONS_MISSING is a
//   definite finding about this page.
// - An iframe, object or embed only shows that a player is present. Whether a YouTube or Canvas Studio video is
//   captioned is a property of the hosted media, which TeachWell does not fetch. MEDIA_CAPTIONS_UNCONFIRMED therefore
//   asks the instructor to confirm, and says plainly that TeachWell could not check it. Guessing from the host would
//   produce confident findings that are wrong, which is worse than asking.
public static class MediaChecks
{
    public const string CaptionsMissing = "MEDIA_CAPTIONS_MISSING", CaptionsUnconfirmed = "MEDIA_CAPTIONS_UNCONFIRMED";

    public static readonly IReadOnlyList<ElementCheck> Rules = [new MissingCaptions(), new UnconfirmedCaptions()];

    private const int Media = 6;
    private const string NotCaptured =
        "This snapshot predates media capture, so video and audio were not checked for captions. Re-import the course to check them.";

    // Describes a player for a finding: its accessible name when it has one, otherwise its position.
    private static string Describe(MediaEvidence m) =>
        m.Title is { Length: > 0 } title ? $"\"{Shorten(title)}\"" : $"{m.Kind.ToLowerInvariant()} {m.Index + 1}";
    private static string Shorten(string value) => value.Length <= 60 ? value : value[..57] + "...";
    private static string Where(MediaEvidence m) => m.Provider is { Length: > 0 } host ? $" hosted at {host}" : "";

    private abstract class MediaCheck : ElementCheck
    {
        public override string Aspect => ReviewAspects.Accessibility;
        public override int Stage => Media;
        // Media evidence lives on ContentEvidence, so the snapshot must have structural evidence at all.
        protected static IReadOnlyList<MediaEvidence>? MediaOf(ElementCheckContext c)
        {
            if (c.Evidence.Media is null) c.Limit(NotCaptured);
            return c.Evidence.Media;
        }
    }

    private sealed class MissingCaptions : MediaCheck
    {
        public override string Code => CaptionsMissing; public override string Name => "Video or audio has no captions";
        public override string DefaultSeverity => "High";
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s) =>
            (MediaOf(c) ?? []).Where(m => m.HasCaptionTrack == false).Select(m => Hit(s, m.Index, $"{m.Index}", "Video or audio has no captions",
                $"The {m.Kind.ToLowerInvariant()} player {Describe(m)} has no captions or subtitles track, so deaf and hard-of-hearing students cannot follow it.",
                m.Kind == MediaKinds.Audio
                    ? "Add a transcript for the audio, either as a captions track on the player or as text on the page next to it."
                    : "Add a captions track to the video. If the media is in Canvas Studio or My Media, open it there, request or edit captions, and publish them.",
                $"{m.Kind} player {m.Index + 1}{Where(m)}: no <track kind=\"captions\"> or \"subtitles\"."));
    }

    private sealed class UnconfirmedCaptions : MediaCheck
    {
        public override string Code => CaptionsUnconfirmed; public override string Name => "Embedded media needs a captions check";
        // Lower than a definite missing-captions finding: the media may well be captioned already.
        public override string DefaultSeverity => "Medium";
        public override IEnumerable<CheckHit> Run(ElementCheckContext c, CheckSettings s)
        {
            var media = MediaOf(c) ?? [];
            // An embed TeachWell does not recognise as a player is reported as not inspected rather than asked about
            // captions. Most are not media: an LTI launch, a document viewer, a form. Asking about every one of them
            // would bury the real findings, and this college reaches its syllabi through an LTI tool (ADR 008).
            if (media.Count(m => m.Kind == MediaKinds.Tool) is var tools && tools > 0)
                c.Limit($"{tools} embedded {(tools == 1 ? "tool was" : "tools were")} not inspected (an external tool, document or widget rather than a recognised video or audio player). If any of them plays video or audio, check its captions where it is hosted.");
            return Captions(c, s, media);
        }

        private IEnumerable<CheckHit> Captions(ElementCheckContext c, CheckSettings s, IReadOnlyList<MediaEvidence> media) =>
            media.Where(m => m.Kind == MediaKinds.Embed).Select(m => Hit(s, m.Index, $"{m.Index}", "Embedded media needs a captions check",
                $"This page embeds a player ({Describe(m)}{Where(m)}). TeachWell cannot see whether the media itself is captioned, because it reads the page and not the video.",
                "Play the media and check that accurate captions are available. If they are, mark this step done, or not applicable if the embed is not video or audio. If they are not, add captions where the media is hosted.",
                $"Embedded player {m.Index + 1}{Where(m)}: captions cannot be determined from the page."));
    }
}
