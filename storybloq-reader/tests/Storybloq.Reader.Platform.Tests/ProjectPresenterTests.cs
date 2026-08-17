using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.State;
using Storybloq.Reader.Core.Watching;
using Storybloq.Reader.Platform.Presentation;
using Xunit;

namespace Storybloq.Reader.Platform.Tests;

/// <summary>
/// Covers what the sidebar actually renders. This is the layer the WinUI project binds to, so
/// asserting it here is what keeps the untestable XAML surface down to layout.
/// </summary>
public class ProjectPresenterTests
{
    private static ProjectPresentation Present(string root, WatcherStatus status = WatcherStatus.Watching, bool isWatching = true)
    {
        var project = new StoryProjectLoader(ResilientFileReader.Immediate).Load(root);
        return ProjectPresenter.Build(ProjectState.From(project), status, isWatching);
    }

    private static string SampleProject
    {
        get
        {
            // The core fixtures live alongside this test project.
            var directory = AppContext.BaseDirectory;
            while (directory is not null && !Directory.Exists(Path.Combine(directory, "tests", "fixtures")))
            {
                directory = Path.GetDirectoryName(directory);
            }

            Assert.NotNull(directory);
            return Path.Combine(directory, "tests", "fixtures", "sample-project");
        }
    }

    [Fact]
    public void ProjectHeaderSummarisesProgressAndCurrentPhase()
    {
        var presentation = Present(SampleProject);

        Assert.Equal("Aurora", presentation.Name);
        Assert.Equal("Live", presentation.StatusText);
        Assert.True(presentation.IsLive);
        Assert.Equal("2/9 complete · 1 in progress · 2 blocked", presentation.Summary);
        Assert.Equal("Editor", presentation.CurrentPhase);
    }

    [Fact]
    public void TheBadgeCombinesBlockedWorkAndSevereIssues()
    {
        // 2 blocked tickets + 3 unresolved critical/high issues.
        Assert.Equal(5, Present(SampleProject).BadgeCount);
    }

    [Fact]
    public void TicketRowsExplainWhyEachBlockedTicketIsStuck()
    {
        var tickets = Present(SampleProject).Tickets;

        var blocked = tickets.Single(ticket => ticket.Id == "T-005");
        Assert.True(blocked.IsBlocked);
        Assert.Equal("T-004 Rich text model", blocked.BlockedBy);

        var unblocked = tickets.Single(ticket => ticket.Id == "T-007");
        Assert.False(unblocked.IsBlocked);
        Assert.Equal(string.Empty, unblocked.BlockedBy);
    }

    /// <summary>
    /// A status from a newer Storybloq is shown as written rather than mislabelled as one of
    /// the statuses this build happens to know.
    /// </summary>
    [Fact]
    public void AnUnknownStatusIsDisplayedVerbatimAndFlagged()
    {
        var ticket = Present(SampleProject).Tickets.Single(row => row.Id == "T-010");

        Assert.Equal("paused", ticket.Status);
        Assert.True(ticket.HasUnknownStatus);
        Assert.False(ticket.IsComplete);
    }

    [Fact]
    public void TicketsAreGroupedByPhaseThenOrdered()
    {
        var tickets = Present(SampleProject).Tickets;

        Assert.Equal(
            ["p1", "p1", "p2", "p2", "p2", "p3", "p3", "p3", "p3"],
            tickets.Select(ticket => ticket.Phase));
        Assert.Equal(["T-002", "T-003"], tickets.Take(2).Select(ticket => ticket.Id));
    }

    [Fact]
    public void UmbrellaAndDeletedTicketsNeverReachTheUi()
    {
        var tickets = Present(SampleProject).Tickets;

        Assert.DoesNotContain(tickets, ticket => ticket.Id == "T-001"); // umbrella
        Assert.DoesNotContain(tickets, ticket => ticket.Id == "T-008"); // deleted
        Assert.Equal(9, tickets.Count);
    }

    [Fact]
    public void IssuesAreOrderedBySeverityWithAttentionFlagged()
    {
        var issues = Present(SampleProject).Issues;

        Assert.Equal(["ISS-001", "ISS-005", "ISS-004", "ISS-003", "ISS-002"], issues.Select(issue => issue.Id));
        Assert.True(issues[0].NeedsAttention);
        Assert.False(issues[^1].NeedsAttention);
        Assert.Equal("editor, sync", issues[0].Components);
    }

    [Fact]
    public void PhaseRowsCarryStatusCountsAndMarkTheCurrentPhase()
    {
        var phases = Present(SampleProject).Phases;

        Assert.Equal(["Foundations", "Editor", "Polish"], phases.Select(phase => phase.Name));
        Assert.Equal(["Complete", "In progress", "Not started"], phases.Select(phase => phase.Status));
        Assert.Equal("2/2", phases[0].Counts);
        Assert.Equal(1d, phases[0].Progress);

        Assert.True(phases[1].IsCurrent);
        Assert.False(phases[0].IsCurrent);
    }

    [Fact]
    public void HandoversAreListedNewestFirstWithReadableTitles()
    {
        var handovers = Present(SampleProject).Handovers;

        Assert.Equal("Phase Two Kickoff", handovers[0].Title);
        Assert.Equal("2026-03-14", handovers[0].Date);
        Assert.Equal("2026-01-15", handovers[^1].Date);
    }

    [Fact]
    public void OnlyUnclearedRoadmapBlockersAreShown()
    {
        var blocker = Assert.Single(Present(SampleProject).Blockers);

        Assert.Equal("Windows signing certificate", blocker.Name);
        Assert.Equal("Still with procurement.", blocker.Note);
        Assert.Equal("2026-03-01", blocker.Since);
    }

    [Fact]
    public void NotesAndLessonsShowOnlyLiveEntries()
    {
        var presentation = Present(SampleProject);

        var note = Assert.Single(presentation.Notes);
        Assert.StartsWith("Untitled note", note.Title);

        var lesson = Assert.Single(presentation.Lessons);
        Assert.Equal("Debounce filesystem events", lesson.Title);
        Assert.Equal("reinforced 3×", lesson.Reinforcements);
        Assert.Equal("postmortem", lesson.Source);
    }

    [Fact]
    public void FeatureFlagsDecideWhichPanelsAreOffered()
    {
        var root = Path.Combine(Path.GetDirectoryName(SampleProject)!, "minimal-project");
        var presentation = Present(root);

        Assert.True(presentation.ShowTickets);
        Assert.True(presentation.ShowIssues);
        Assert.False(presentation.ShowHandovers);
        Assert.False(presentation.ShowRoadmap);
        Assert.Equal("No tickets", presentation.Summary);
        Assert.Equal("No roadmap", presentation.CurrentPhase);
    }

    [Fact]
    public void LoadProblemsAreSurfacedRatherThanSwallowed()
    {
        var root = Path.Combine(Path.GetDirectoryName(SampleProject)!, "broken-project");
        var diagnostics = Present(root).Diagnostics;

        Assert.NotEmpty(diagnostics);
        Assert.Contains(diagnostics, diagnostic => diagnostic.File == "T-002.json" && diagnostic.Severity == "Error");
    }

    [Theory]
    [InlineData(WatcherStatus.Watching, true, "Live")]
    [InlineData(WatcherStatus.AwaitingStoryDirectory, true, "Waiting for .story to be created")]
    [InlineData(WatcherStatus.Unavailable, true, "Folder unavailable")]
    [InlineData(WatcherStatus.Faulted, true, "Watch failed — retrying")]
    [InlineData(WatcherStatus.Watching, false, "Not watching — will refresh when selected")]
    public void EveryWatchStateHasAnHonestLabel(WatcherStatus status, bool isWatching, string expected) =>
        Assert.Equal(expected, ProjectPresenter.DescribeStatus(status, isWatching));

    [Fact]
    public void AttentionRowsLabelTheirKind()
    {
        var project = new StoryProjectLoader(ResilientFileReader.Immediate).Load(SampleProject);
        var rows = ProjectPresenter.BuildAttention(AttentionQuery.Collect([ProjectState.From(project)]));

        Assert.Contains(rows, row => row.Kind == "Issue");
        Assert.Contains(rows, row => row.Kind == "Blocked");
        Assert.Contains(rows, row => row.Kind == "Roadmap");
        Assert.All(rows, row => Assert.Equal("Aurora", row.ProjectName));
    }
}
