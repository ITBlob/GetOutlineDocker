using Storybloq.Reader.Core.Models;
using Storybloq.Reader.Core.State;
using Xunit;

namespace Storybloq.Reader.Core.Tests;

/// <summary>
/// These assertions pin the aggregation rules to upstream's, because numbers that disagree with
/// <c>storybloq status</c> would make the reader worse than useless.
/// </summary>
public class ProjectStateTests
{
    private static ProjectState Sample => Fixtures.State(Fixtures.SampleProject);

    [Fact]
    public void CountsAreTakenOverLeafTicketsOnly()
    {
        var state = Sample;

        // 11 ticket files: one deleted, one an umbrella with two active children.
        Assert.Equal(10, state.ActiveTickets.Count);
        Assert.Equal(9, state.LeafTickets.Count);
        Assert.DoesNotContain(state.LeafTickets, ticket => ticket.Id == "T-001");
    }

    [Fact]
    public void StatusCountsIgnoreUnrecognisedStatuses()
    {
        var counts = Sample.Counts;

        Assert.Equal(9, counts.Total);
        Assert.Equal(5, counts.Open);
        Assert.Equal(1, counts.InProgress);
        Assert.Equal(2, counts.Complete);

        // T-010 carries a status this reader does not know; it is counted in the total but
        // claimed by none of the buckets.
        Assert.Equal(8, counts.Open + counts.InProgress + counts.Complete);
    }

    [Fact]
    public void DeletedTicketsAreExcludedEverywhere()
    {
        var state = Sample;

        Assert.DoesNotContain(state.ActiveTickets, ticket => ticket.Id == "T-008");
        Assert.DoesNotContain(state.LeafTickets, ticket => ticket.Id == "T-008");
    }

    [Fact]
    public void BlockedCountMatchesTheBlockedSet()
    {
        var state = Sample;

        Assert.Equal(2, state.Counts.Blocked);
        Assert.Equal(
            ["T-005", "T-006"],
            state.BlockedTickets.Select(ticket => ticket.Id).OrderBy(id => id, StringComparer.Ordinal));
    }

    [Fact]
    public void AnUnresolvableBlockerReferenceCountsAsBlocking()
    {
        var state = Sample;
        var ticket = state.LeafTickets.Single(t => t.Id == "T-006");

        Assert.True(state.IsBlocked(ticket));
        Assert.Equal(["T-999 (unknown)"], state.DescribeBlockers(ticket));
    }

    [Fact]
    public void ACompletedBlockerDoesNotBlock()
    {
        var state = Sample;
        var ticket = state.LeafTickets.Single(t => t.Id == "T-007");

        Assert.False(state.IsBlocked(ticket));
        Assert.Empty(state.DescribeBlockers(ticket));
    }

    [Fact]
    public void ADeletedBlockerDoesNotBlock()
    {
        var state = Sample;
        var ticket = state.LeafTickets.Single(t => t.Id == "T-011");

        Assert.False(state.IsBlocked(ticket));
    }

    [Fact]
    public void BlockerDescriptionsNameTheOffendingTicket()
    {
        var state = Sample;
        var ticket = state.LeafTickets.Single(t => t.Id == "T-005");

        Assert.Equal(["T-004 Rich text model"], state.DescribeBlockers(ticket));
    }

    [Fact]
    public void PhaseStatusIsRolledUpFromLeavesAndIgnoresTheUmbrellasOwnStatus()
    {
        var state = Sample;

        var p1 = state.Phases.Single(phase => phase.Id == "p1");
        var p2 = state.Phases.Single(phase => phase.Id == "p2");
        var p3 = state.Phases.Single(phase => phase.Id == "p3");

        // T-001 sits in p1 and is still "open", but it is an umbrella: both its children are
        // complete, so the phase is complete.
        Assert.Equal(PhaseStatus.Complete, p1.Status);
        Assert.Equal(2, p1.Counts.Total);

        Assert.Equal(PhaseStatus.InProgress, p2.Status);
        Assert.Equal(3, p2.Counts.Total);
        Assert.Equal(2, p2.Counts.Blocked);

        Assert.Equal(PhaseStatus.NotStarted, p3.Status);
        Assert.Equal(4, p3.Counts.Total);
    }

    [Fact]
    public void PhaseTotalsAccountForEveryLeafTicket()
    {
        var state = Sample;
        Assert.Equal(state.Counts.Total, state.Phases.Sum(phase => phase.Counts.Total));
    }

    [Fact]
    public void CurrentPhaseIsTheFirstIncompleteOneInRoadmapOrder()
    {
        Assert.Equal("p2", Sample.CurrentPhase?.Id);
    }

    [Fact]
    public void AttentionIssuesAreUnresolvedCriticalAndHighWorstFirst()
    {
        var state = Sample;

        Assert.Equal(["ISS-001", "ISS-005", "ISS-004"], state.AttentionIssues.Select(issue => issue.Label));
        Assert.DoesNotContain(state.AttentionIssues, issue => issue.Label == "ISS-002"); // resolved
        Assert.DoesNotContain(state.AttentionIssues, issue => issue.Label == "ISS-003"); // medium
    }

    [Fact]
    public void RoadmapBlockersHonourBothTheLegacyFlagAndTheMigratedDate()
    {
        var blocker = Assert.Single(Sample.UnclearedBlockers);

        Assert.Equal("Windows signing certificate", blocker.Name);

        var all = Sample.Project.Roadmap!.BlockerList;
        Assert.True(all.Single(b => b.Name == "Design review sign-off").IsCleared);   // legacy cleared: true
        Assert.True(all.Single(b => b.Name == "Upstream API contract").IsCleared);    // migrated clearedDate
    }

    [Fact]
    public void TicketsResolveByIdDisplayIdAndSupersededDisplayId()
    {
        var state = Sample;

        Assert.Equal("t-0abcdefghjkmnpqr", state.ResolveTicket("t-0abcdefghjkmnpqr")?.Id);
        Assert.Equal("t-0abcdefghjkmnpqr", state.ResolveTicket("T-009")?.Id);
        Assert.Equal("t-0abcdefghjkmnpqr", state.ResolveTicket("T-104")?.Id);
        Assert.Null(state.ResolveTicket("T-999"));
        Assert.Null(state.ResolveTicket(null));
    }

    [Fact]
    public void ArchivedNotesAndSupersededLessonsAreFilteredFromTheActiveViews()
    {
        var state = Sample;

        Assert.Equal(["N-001"], state.ActiveNotes.Select(note => note.Id));
        Assert.Equal(["L-001"], state.ActiveLessons.Select(lesson => lesson.Id));
    }

    [Fact]
    public void AnUntitledNoteFallsBackToItsFirstLine()
    {
        var note = Sample.ActiveNotes.Single();

        Assert.Null(note.Title);
        Assert.Equal("Untitled note: the reader should fall back to the first line for display.", note.DisplayTitle);
    }

    [Fact]
    public void AProjectWithNoRoadmapHasNoPhases()
    {
        var state = Fixtures.State(Fixtures.MinimalProject);

        Assert.Empty(state.Phases);
        Assert.Null(state.CurrentPhase);
        Assert.Equal(TicketCounts.Empty, state.Counts);
    }
}
