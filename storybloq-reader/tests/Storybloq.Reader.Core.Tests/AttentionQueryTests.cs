using Storybloq.Reader.Core.State;
using Xunit;

namespace Storybloq.Reader.Core.Tests;

public class AttentionQueryTests
{
    private static IReadOnlyList<AttentionItem> AcrossBothProjects() =>
        AttentionQuery.Collect([Fixtures.State(Fixtures.SampleProject), Fixtures.State(Fixtures.SecondProject)]);

    [Fact]
    public void CriticalIssuesLeadTheListAcrossEveryProject()
    {
        var items = AcrossBothProjects();

        Assert.Equal(
            [("Aurora", "ISS-001"), ("Aurora", "ISS-005"), ("Beacon", "ISS-001")],
            items.TakeWhile(item => item.Rank == 0).Select(item => (item.ProjectName, item.Reference)));
    }

    [Fact]
    public void BlockedWorkOutranksLowerSeverityIssues()
    {
        var items = AcrossBothProjects();

        var firstBlocked = items.ToList().FindIndex(item => item.Kind == AttentionKind.BlockedTicket);
        var highIssue = items.ToList().FindIndex(item => item.Reference == "ISS-004");

        Assert.True(firstBlocked < highIssue);
    }

    [Fact]
    public void EveryBlockedTicketFromEveryProjectIsListedWithItsCause()
    {
        var items = AcrossBothProjects();
        var blocked = items.Where(item => item.Kind == AttentionKind.BlockedTicket).ToList();

        Assert.Equal(
            [("Aurora", "T-005"), ("Aurora", "T-006"), ("Beacon", "T-001")],
            blocked.Select(item => (item.ProjectName, item.Reference)));

        Assert.Equal("Blocked by T-004 Rich text model", blocked[0].Detail);
        Assert.Equal("Blocked by T-404 (unknown)", blocked[2].Detail);
    }

    [Fact]
    public void UnclearedRoadmapBlockersComeLast()
    {
        var items = AcrossBothProjects();
        var last = items[^1];

        Assert.Equal(AttentionKind.RoadmapBlocker, last.Kind);
        Assert.Equal("Windows signing certificate", last.Title);
        Assert.Equal("Still with procurement.", last.Detail);
    }

    [Fact]
    public void ItemsCarryTheProjectTheyCameFrom()
    {
        var items = AcrossBothProjects();

        Assert.All(items, item => Assert.False(string.IsNullOrEmpty(item.ProjectRoot)));
        Assert.Contains(items, item => item.ProjectName == "Beacon");
        Assert.Contains(items, item => item.ProjectName == "Aurora");
    }

    [Fact]
    public void AHealthyProjectContributesNothing()
    {
        Assert.Empty(AttentionQuery.Collect([Fixtures.State(Fixtures.MinimalProject)]));
    }

    [Fact]
    public void IssueDetailsNameTheSeverityAndLocation()
    {
        var items = AcrossBothProjects();
        var issue = items.First(item => item.Reference == "ISS-001" && item.ProjectName == "Beacon");

        Assert.Equal("Credentials logged in plaintext", issue.Title);
        Assert.Equal("critical · auth/session.py · Secrets reach the log sink.", issue.Detail);
    }
}
