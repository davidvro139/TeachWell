using TeachWell.Domain;
using Xunit;

public class SupportUnitTests
{
    [Fact] public void Transition_table_matches_the_documented_status_machine()
    {
        Assert.Equal(
            new[] { (SupportVocabulary.Triaged, SupportVocabulary.Responder, false), (SupportVocabulary.InProgress, SupportVocabulary.Responder, false) },
            SupportVocabulary.From(SupportVocabulary.New).Select(t => (t.To, t.Actor, t.ReasonRequired)));
        Assert.Equal(
            new[] { (SupportVocabulary.WaitingOnInstructor, SupportVocabulary.Responder, true), (SupportVocabulary.Resolved, SupportVocabulary.Responder, true) },
            SupportVocabulary.From(SupportVocabulary.InProgress).Select(t => (t.To, t.Actor, t.ReasonRequired)));
        Assert.Contains((SupportVocabulary.InProgress, SupportVocabulary.Either, false), SupportVocabulary.From(SupportVocabulary.WaitingOnInstructor).Select(t => (t.To, t.Actor, t.ReasonRequired)));
        // Resolved and Closed both reopen directly to InProgress — this is the only path back in, and it always requires a reason.
        Assert.Equal([(SupportVocabulary.Closed, SupportVocabulary.Either, false), (SupportVocabulary.InProgress, SupportVocabulary.Either, true)],
            SupportVocabulary.From(SupportVocabulary.Resolved).Select(t => (t.To, t.Actor, t.ReasonRequired)));
        Assert.Equal([(SupportVocabulary.InProgress, SupportVocabulary.Either, true)], SupportVocabulary.From(SupportVocabulary.Closed).Select(t => (t.To, t.Actor, t.ReasonRequired)));
        // Unlike revision tasks, no ticket status is a dead end: every state has at least one way forward.
        Assert.All(SupportVocabulary.All, s => Assert.NotEmpty(SupportVocabulary.From(s)));
    }
}
