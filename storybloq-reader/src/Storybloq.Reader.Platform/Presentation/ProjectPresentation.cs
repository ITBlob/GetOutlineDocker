using System.Globalization;
using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.Models;
using Storybloq.Reader.Core.State;
using Storybloq.Reader.Core.Watching;

namespace Storybloq.Reader.Platform.Presentation;

/// <summary>
/// Flattened, already-formatted rows for the UI to bind to.
/// </summary>
/// <remarks>
/// Every string the sidebar shows is produced here rather than in XAML or a converter. That is
/// what keeps the display logic — status text for unknown enum values, blocker explanations,
/// count summaries — inside the test suite that runs on every platform, and leaves the WinUI
/// layer with nothing but layout and thread marshalling, which is the part no CI job can
/// meaningfully assert on anyway.
/// </remarks>
public sealed record TicketRow(
    string Id,
    string Title,
    string Status,
    string Type,
    string Phase,
    bool IsBlocked,
    string BlockedBy,
    bool IsComplete,
    bool HasUnknownStatus);

public sealed record IssueRow(
    string Id,
    string Title,
    string Severity,
    string Status,
    string Location,
    string Impact,
    string Components,
    bool NeedsAttention);

public sealed record HandoverRow(string Title, string FileName, string Date, string Path);

public sealed record NoteRow(string Id, string Title, string Content, string Tags);

public sealed record LessonRow(string Id, string Title, string Content, string Context, string Source, string Tags, string Reinforcements);

public sealed record PhaseRow(string Id, string Name, string Status, string Summary, string Counts, double Progress, bool IsCurrent);

public sealed record BlockerRow(string Name, string Note, string Since);

public sealed record DiagnosticRow(string Severity, string File, string Message);

public sealed record AttentionRow(string ProjectName, string Kind, string Reference, string Title, string Detail);

/// <summary>Everything one project contributes to the UI, in bind-ready form.</summary>
public sealed class ProjectPresentation
{
    public required string Name { get; init; }

    public required string Root { get; init; }

    /// <summary>One line describing the watch state, shown under the project name.</summary>
    public required string StatusText { get; init; }

    public required bool IsLive { get; init; }

    public required string Summary { get; init; }

    public required string CurrentPhase { get; init; }

    public required double Progress { get; init; }

    /// <summary>Blocked work plus unresolved critical and high issues — the rail badge.</summary>
    public required int BadgeCount { get; init; }

    public required IReadOnlyList<PhaseRow> Phases { get; init; }

    public required IReadOnlyList<TicketRow> Tickets { get; init; }

    public required IReadOnlyList<IssueRow> Issues { get; init; }

    public required IReadOnlyList<HandoverRow> Handovers { get; init; }

    public required IReadOnlyList<NoteRow> Notes { get; init; }

    public required IReadOnlyList<LessonRow> Lessons { get; init; }

    public required IReadOnlyList<BlockerRow> Blockers { get; init; }

    public required IReadOnlyList<DiagnosticRow> Diagnostics { get; init; }

    public required bool ShowTickets { get; init; }

    public required bool ShowIssues { get; init; }

    public required bool ShowHandovers { get; init; }

    public required bool ShowRoadmap { get; init; }
}

public static class ProjectPresenter
{
    public static ProjectPresentation Build(ProjectState state, WatcherStatus watcherStatus, bool isWatching)
    {
        ArgumentNullException.ThrowIfNull(state);

        var project = state.Project;
        var features = project.Features;
        var counts = state.Counts;

        return new ProjectPresentation
        {
            Name = project.DisplayName,
            Root = project.Root,
            StatusText = DescribeStatus(watcherStatus, isWatching),
            IsLive = isWatching && watcherStatus == WatcherStatus.Watching,
            Summary = DescribeCounts(counts),
            CurrentPhase = state.CurrentPhase?.DisplayName ?? (state.Phases.Count == 0 ? "No roadmap" : "All phases complete"),
            Progress = counts.CompletionFraction,
            BadgeCount = counts.Blocked + state.AttentionIssues.Count,
            Phases = BuildPhases(state),
            Tickets = BuildTickets(state),
            Issues = BuildIssues(state),
            Handovers = project.Handovers.Select(BuildHandover).ToList(),
            Notes = state.ActiveNotes.Select(BuildNote).ToList(),
            Lessons = state.ActiveLessons.Select(BuildLesson).ToList(),
            Blockers = state.UnclearedBlockers.Select(BuildBlocker).ToList(),
            Diagnostics = project.Diagnostics.Select(BuildDiagnostic).ToList(),
            ShowTickets = features.TicketsEnabled,
            ShowIssues = features.IssuesEnabled,
            ShowHandovers = features.HandoversEnabled,
            ShowRoadmap = features.RoadmapEnabled,
        };
    }

    public static IReadOnlyList<AttentionRow> BuildAttention(IEnumerable<AttentionItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return items.Select(item => new AttentionRow(
            item.ProjectName,
            item.Kind switch
            {
                AttentionKind.Issue => "Issue",
                AttentionKind.BlockedTicket => "Blocked",
                AttentionKind.RoadmapBlocker => "Roadmap",
                _ => "Other",
            },
            item.Reference,
            item.Title,
            item.Detail)).ToList();
    }

    public static string DescribeStatus(WatcherStatus status, bool isWatching) => (isWatching, status) switch
    {
        (false, _) => "Not watching — will refresh when selected",
        (_, WatcherStatus.Watching) => "Live",
        (_, WatcherStatus.AwaitingStoryDirectory) => "Waiting for .story to be created",
        (_, WatcherStatus.Unavailable) => "Folder unavailable",
        (_, WatcherStatus.Faulted) => "Watch failed — retrying",
        _ => "Unknown",
    };

    public static string DescribeCounts(TicketCounts counts)
    {
        if (counts.Total == 0)
        {
            return "No tickets";
        }

        var summary = $"{counts.Complete}/{counts.Total} complete";
        if (counts.InProgress > 0)
        {
            summary += $" · {counts.InProgress} in progress";
        }

        if (counts.Blocked > 0)
        {
            summary += $" · {counts.Blocked} blocked";
        }

        return summary;
    }

    private static IReadOnlyList<PhaseRow> BuildPhases(ProjectState state) =>
        state.Phases.Select(phase => new PhaseRow(
            phase.Id,
            phase.DisplayName,
            phase.Status switch
            {
                PhaseStatus.Complete => "Complete",
                PhaseStatus.InProgress => "In progress",
                _ => "Not started",
            },
            phase.Phase.Summary ?? phase.Phase.Description ?? string.Empty,
            phase.Counts.Total == 0
                ? "No tickets"
                : $"{phase.Counts.Complete}/{phase.Counts.Total}",
            phase.Counts.CompletionFraction,
            ReferenceEquals(phase, state.CurrentPhase))).ToList();

    private static IReadOnlyList<TicketRow> BuildTickets(ProjectState state) =>
        state.LeafTickets
            .OrderBy(ticket => ticket.Phase ?? "￿", StringComparer.OrdinalIgnoreCase)
            .ThenBy(ticket => ticket.Order ?? int.MaxValue)
            .ThenBy(ticket => ticket.Label, StringComparer.OrdinalIgnoreCase)
            .Select(ticket => new TicketRow(
                ticket.Label,
                ticket.Title ?? "(untitled)",
                ticket.Status.DisplayText,
                ticket.Type.DisplayText,
                ticket.Phase ?? "Unassigned",
                state.IsBlocked(ticket),
                string.Join(", ", state.DescribeBlockers(ticket)),
                ticket.IsComplete,
                ticket.Status.IsUnrecognised))
            .ToList();

    private static IReadOnlyList<IssueRow> BuildIssues(ProjectState state) =>
        state.ActiveIssues
            .OrderBy(issue => issue.IsResolved)
            .ThenBy(issue => issue.Severity.SortOrder())
            .ThenBy(issue => issue.Label, StringComparer.OrdinalIgnoreCase)
            .Select(issue => new IssueRow(
                issue.Label,
                issue.Title ?? "(untitled)",
                issue.Severity.DisplayText,
                issue.Status.DisplayText,
                issue.Location ?? string.Empty,
                issue.Impact ?? string.Empty,
                string.Join(", ", issue.Components ?? []),
                issue.NeedsAttention))
            .ToList();

    private static HandoverRow BuildHandover(HandoverFile handover) => new(
        handover.Title,
        handover.FileName,
        handover.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "undated",
        handover.Path);

    private static NoteRow BuildNote(Note note) => new(
        note.Label,
        note.DisplayTitle,
        note.Content ?? string.Empty,
        string.Join(", ", note.Tags ?? []));

    private static LessonRow BuildLesson(Lesson lesson) => new(
        lesson.Label,
        lesson.Title ?? "(untitled)",
        lesson.Content ?? string.Empty,
        lesson.Context ?? string.Empty,
        lesson.Source.DisplayText,
        string.Join(", ", lesson.Tags ?? []),
        lesson.Reinforcements is > 0 ? $"reinforced {lesson.Reinforcements}×" : string.Empty);

    private static BlockerRow BuildBlocker(Blocker blocker) => new(
        blocker.Name ?? "(unnamed)",
        blocker.Note ?? string.Empty,
        blocker.CreatedDate ?? string.Empty);

    private static DiagnosticRow BuildDiagnostic(LoadDiagnostic diagnostic) => new(
        diagnostic.Severity == DiagnosticSeverity.Error ? "Error" : "Warning",
        diagnostic.FileName,
        diagnostic.Message);
}
