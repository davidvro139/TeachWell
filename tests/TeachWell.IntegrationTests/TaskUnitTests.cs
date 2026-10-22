using TeachWell.Domain;
using Xunit;

public class TaskUnitTests
{
    [Fact] public void Transition_table_matches_the_documented_state_machine()
    {
        Assert.Equal(
            new[] { (TaskVocabulary.NeedsRevision, TaskVocabulary.Creator, false), (TaskVocabulary.Withdrawn, TaskVocabulary.Creator, true) },
            TaskVocabulary.From(TaskVocabulary.Draft).Select(t => (t.To, t.Actor, t.ReasonRequired)));
        Assert.Equal(
            new[] { (TaskVocabulary.ReadyForReview, TaskVocabulary.Assignee, false), (TaskVocabulary.Withdrawn, TaskVocabulary.Creator, true), (TaskVocabulary.NotApplicable, TaskVocabulary.Creator, true) },
            TaskVocabulary.From(TaskVocabulary.InProgress).Select(t => (t.To, t.Actor, t.ReasonRequired)));
        Assert.Contains((TaskVocabulary.InProgress, TaskVocabulary.Assignee, false), TaskVocabulary.From(TaskVocabulary.ChangesRequested).Select(t => (t.To, t.Actor, t.ReasonRequired)));
        Assert.Contains((TaskVocabulary.ReadyForReview, TaskVocabulary.Assignee, false), TaskVocabulary.From(TaskVocabulary.ChangesRequested).Select(t => (t.To, t.Actor, t.ReasonRequired)));
        // Terminal states and Blocked have no documented forward edges in the table; Blocked/Resume are handled as service-layer specials.
        foreach (var terminal in TaskVocabulary.Terminal) Assert.Empty(TaskVocabulary.From(terminal));
        Assert.Empty(TaskVocabulary.From(TaskVocabulary.Blocked));
        Assert.Equal(["NeedsRevision", "InProgress", "ReadyForReview", "ChangesRequested"], TaskVocabulary.Blockable);
    }
    [Fact] public void Diff_reports_unchanged_added_and_removed_lines_without_assuming_equal_length()
    {
        var diff = TextDiff.Compute("Keep this line.\nDelete this one.\nKeep this too.", "Keep this line.\nKeep this too.\nAdd this new line.");
        Assert.Equal(
            [("Same", "Keep this line."), ("Removed", "Delete this one."), ("Same", "Keep this too."), ("Added", "Add this new line.")],
            diff.Select(d => (d.Kind, d.Text)));
    }
    [Fact] public void Diff_of_identical_or_empty_text_has_no_changes()
    {
        Assert.All(TextDiff.Compute("Same text.\nSecond line.", "Same text.\nSecond line."), d => Assert.Equal("Same", d.Kind));
        Assert.Empty(TextDiff.Compute("", ""));
        Assert.All(TextDiff.Compute("", "New content."), d => Assert.Equal("Added", d.Kind));
        Assert.All(TextDiff.Compute("Old content.", ""), d => Assert.Equal("Removed", d.Kind));
    }
}
