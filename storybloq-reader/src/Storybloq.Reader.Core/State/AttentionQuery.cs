using Storybloq.Reader.Core.Models;

namespace Storybloq.Reader.Core.State;

public enum AttentionKind
{
    Issue,
    BlockedTicket,
    RoadmapBlocker,
}

/// <summary>One thing, in one project, that is asking for the user's attention.</summary>
public sealed record AttentionItem(
    string ProjectName,
    string ProjectRoot,
    AttentionKind Kind,
    string Reference,
    string Title,
    string Detail,
    int Rank);

/// <summary>
/// Rolls the "what is on fire" signals up across every open project.
/// </summary>
/// <remarks>
/// This is the reason the multi-project switcher exists rather than several windows: when an
/// agent is working across repositories, the question is not "how is this project doing" but
/// "where is something stuck".
/// </remarks>
public static class AttentionQuery
{
    // Ranks interleave issue severity with the other kinds so one ordered list reads sensibly:
    // critical issues, then blocked work, then high issues, then roadmap blockers.
    private const int CriticalIssueRank = 0;
    private const int BlockedTicketRank = 10;
    private const int HighIssueRank = 20;
    private const int RoadmapBlockerRank = 30;

    public static IReadOnlyList<AttentionItem> Collect(IEnumerable<ProjectState> states)
    {
        ArgumentNullException.ThrowIfNull(states);

        var items = new List<AttentionItem>();
        foreach (var state in states)
        {
            items.AddRange(CollectFor(state));
        }

        return items
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.ProjectName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Reference, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static IEnumerable<AttentionItem> CollectFor(ProjectState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var projectName = state.Project.DisplayName;
        var root = state.Project.Root;

        foreach (var issue in state.AttentionIssues)
        {
            yield return new AttentionItem(
                projectName,
                root,
                AttentionKind.Issue,
                issue.Label,
                issue.Title ?? "(untitled issue)",
                Describe(issue),
                issue.Severity.Is(IssueSeverity.Critical) ? CriticalIssueRank : HighIssueRank);
        }

        foreach (var ticket in state.BlockedTickets)
        {
            var blockers = state.DescribeBlockers(ticket);
            yield return new AttentionItem(
                projectName,
                root,
                AttentionKind.BlockedTicket,
                ticket.Label,
                ticket.Title ?? "(untitled ticket)",
                blockers.Count == 0 ? "Blocked" : "Blocked by " + string.Join(", ", blockers),
                BlockedTicketRank);
        }

        foreach (var blocker in state.UnclearedBlockers)
        {
            yield return new AttentionItem(
                projectName,
                root,
                AttentionKind.RoadmapBlocker,
                "roadmap",
                blocker.Name ?? "(unnamed blocker)",
                blocker.Note ?? "Uncleared roadmap blocker",
                RoadmapBlockerRank);
        }
    }

    private static string Describe(Issue issue)
    {
        var severity = issue.Severity.DisplayText;
        var location = string.IsNullOrWhiteSpace(issue.Location) ? null : issue.Location;
        var impact = string.IsNullOrWhiteSpace(issue.Impact) ? null : issue.Impact;

        return (location, impact) switch
        {
            (null, null) => severity,
            (not null, null) => $"{severity} · {location}",
            (null, not null) => $"{severity} · {impact}",
            _ => $"{severity} · {location} · {impact}",
        };
    }
}
