using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.Models;

namespace Storybloq.Reader.Core.State;

public readonly record struct TicketCounts(int Total, int Open, int InProgress, int Complete, int Blocked)
{
    public static TicketCounts Empty => default;

    public double CompletionFraction => Total == 0 ? 0d : (double)Complete / Total;
}

public sealed record PhaseSummary(Phase Phase, PhaseStatus Status, TicketCounts Counts)
{
    public string Id => Phase.Id ?? string.Empty;

    public string DisplayName => Phase.DisplayName;
}

/// <summary>
/// Derived project state: the counts, blocked set, and phase rollup the sidebar displays.
/// </summary>
/// <remarks>
/// The rules here are deliberately identical to upstream's <c>ProjectState</c> pipeline, because
/// a reader that quietly disagrees with <c>storybloq status</c> is worse than no reader at all.
/// Specifically:
/// <list type="bullet">
///   <item>counts are over <em>leaf</em> tickets — a ticket with active children is an umbrella
///   and contributes nothing of its own;</item>
///   <item>a ticket is blocked when any <c>blockedBy</c> reference points at a ticket that is
///   neither complete nor deleted, and an <em>unresolvable</em> reference counts as blocking;</item>
///   <item>phase status is rolled up from leaf tickets, ignoring any status stored on the
///   umbrella itself.</item>
/// </list>
/// </remarks>
public sealed class ProjectState
{
    private readonly Dictionary<string, Ticket> _ticketsByIdentifier;
    private readonly HashSet<string> _blockedTicketIds;

    private ProjectState(
        StoryProject project,
        IReadOnlyList<Ticket> activeTickets,
        IReadOnlyList<Ticket> leafTickets,
        IReadOnlyList<Ticket> blockedTickets,
        Dictionary<string, Ticket> ticketsByIdentifier,
        HashSet<string> blockedTicketIds,
        TicketCounts counts,
        IReadOnlyList<PhaseSummary> phases,
        PhaseSummary? currentPhase,
        IReadOnlyList<Issue> activeIssues,
        IReadOnlyList<Issue> attentionIssues,
        IReadOnlyList<Blocker> unclearedBlockers)
    {
        Project = project;
        ActiveTickets = activeTickets;
        LeafTickets = leafTickets;
        BlockedTickets = blockedTickets;
        _ticketsByIdentifier = ticketsByIdentifier;
        _blockedTicketIds = blockedTicketIds;
        Counts = counts;
        Phases = phases;
        CurrentPhase = currentPhase;
        ActiveIssues = activeIssues;
        AttentionIssues = attentionIssues;
        UnclearedBlockers = unclearedBlockers;
    }

    public StoryProject Project { get; }

    /// <summary>Tickets that are neither archived nor deleted, umbrellas included.</summary>
    public IReadOnlyList<Ticket> ActiveTickets { get; }

    /// <summary>Active tickets with no active children. The population every count is taken over.</summary>
    public IReadOnlyList<Ticket> LeafTickets { get; }

    public IReadOnlyList<Ticket> BlockedTickets { get; }

    public TicketCounts Counts { get; }

    public IReadOnlyList<PhaseSummary> Phases { get; }

    /// <summary>The first phase in roadmap order that is not complete.</summary>
    public PhaseSummary? CurrentPhase { get; }

    public IReadOnlyList<Issue> ActiveIssues { get; }

    /// <summary>Unresolved critical and high issues, worst first.</summary>
    public IReadOnlyList<Issue> AttentionIssues { get; }

    public IReadOnlyList<Blocker> UnclearedBlockers { get; }

    public IReadOnlyList<Note> ActiveNotes =>
        Project.Notes.Where(note => note.Lifecycle.IsActive() && !note.Status.Is(NoteStatus.Archived)).ToList();

    public IReadOnlyList<Lesson> ActiveLessons =>
        Project.Lessons.Where(lesson => lesson.Lifecycle.IsActive() && lesson.IsCurrent).ToList();

    public bool IsBlocked(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return ticket.Id is not null && _blockedTicketIds.Contains(ticket.Id);
    }

    /// <summary>Resolves a reference against ids, display ids, and superseded display ids.</summary>
    public Ticket? ResolveTicket(string? identifier) =>
        string.IsNullOrWhiteSpace(identifier) ? null : _ticketsByIdentifier.GetValueOrDefault(identifier);

    /// <summary>The unresolved blockers of a ticket, for the "why is this stuck" tooltip.</summary>
    public IReadOnlyList<string> DescribeBlockers(Ticket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);

        if (ticket.BlockedBy is null)
        {
            return [];
        }

        var descriptions = new List<string>();
        foreach (var reference in ticket.BlockedBy)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            var blocker = ResolveTicket(reference);
            if (blocker is null)
            {
                descriptions.Add($"{reference} (unknown)");
            }
            else if (!blocker.IsComplete && !blocker.Lifecycle.IsDeleted())
            {
                descriptions.Add($"{blocker.Label} {blocker.Title}".TrimEnd());
            }
        }

        return descriptions;
    }

    public static ProjectState From(StoryProject project)
    {
        ArgumentNullException.ThrowIfNull(project);

        var activeTickets = project.Tickets.Where(ticket => ticket.Lifecycle.IsActive()).ToList();

        // Every identifier a ticket answers to, so references written before a renumbering
        // still resolve. First writer wins on collision; a duplicate id is a corrupt project,
        // not something to crash over.
        var ticketsByIdentifier = new Dictionary<string, Ticket>(StringComparer.Ordinal);
        foreach (var ticket in project.Tickets)
        {
            foreach (var identifier in ticket.AllIdentifiers())
            {
                ticketsByIdentifier.TryAdd(identifier, ticket);
            }
        }

        // A ticket is an umbrella when at least one *active* ticket names it as parent.
        var umbrellaIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ticket in activeTickets)
        {
            if (string.IsNullOrWhiteSpace(ticket.ParentTicket))
            {
                continue;
            }

            var parent = ticketsByIdentifier.GetValueOrDefault(ticket.ParentTicket);
            if (parent?.Id is not null && parent.Lifecycle.IsActive())
            {
                umbrellaIds.Add(parent.Id);
            }
        }

        var leafTickets = activeTickets
            .Where(ticket => ticket.Id is null || !umbrellaIds.Contains(ticket.Id))
            .ToList();

        var blockedTicketIds = new HashSet<string>(StringComparer.Ordinal);
        var blockedTickets = new List<Ticket>();
        foreach (var ticket in leafTickets)
        {
            if (!ComputeIsBlocked(ticket, ticketsByIdentifier))
            {
                continue;
            }

            blockedTickets.Add(ticket);
            if (ticket.Id is not null)
            {
                blockedTicketIds.Add(ticket.Id);
            }
        }

        var counts = new TicketCounts(
            leafTickets.Count,
            leafTickets.Count(ticket => ticket.Status.Is(TicketStatus.Open)),
            leafTickets.Count(ticket => ticket.Status.Is(TicketStatus.InProgress)),
            leafTickets.Count(ticket => ticket.Status.Is(TicketStatus.Complete)),
            blockedTickets.Count);

        var phases = BuildPhaseSummaries(project.Roadmap, leafTickets, blockedTicketIds);
        var currentPhase = phases.FirstOrDefault(phase => phase.Status != PhaseStatus.Complete);

        var activeIssues = project.Issues.Where(issue => issue.Lifecycle.IsActive()).ToList();
        var attentionIssues = activeIssues
            .Where(issue => issue.NeedsAttention)
            .OrderBy(issue => issue.Severity.SortOrder())
            .ThenBy(issue => issue.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var unclearedBlockers = project.Roadmap?.BlockerList
            .Where(blocker => !blocker.IsCleared)
            .ToList() ?? [];

        return new ProjectState(
            project,
            activeTickets,
            leafTickets,
            blockedTickets,
            ticketsByIdentifier,
            blockedTicketIds,
            counts,
            phases,
            currentPhase,
            activeIssues,
            attentionIssues,
            unclearedBlockers);
    }

    private static bool ComputeIsBlocked(Ticket ticket, Dictionary<string, Ticket> ticketsByIdentifier)
    {
        if (ticket.IsComplete || ticket.BlockedBy is null)
        {
            return false;
        }

        foreach (var reference in ticket.BlockedBy)
        {
            if (string.IsNullOrWhiteSpace(reference))
            {
                continue;
            }

            var blocker = ticketsByIdentifier.GetValueOrDefault(reference);

            // An unresolvable reference is treated as blocking: the safe reading of "this
            // depends on something I cannot see" is that the work is not ready.
            if (blocker is null)
            {
                return true;
            }

            if (blocker.Lifecycle.IsDeleted())
            {
                continue;
            }

            if (!blocker.IsComplete)
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyList<PhaseSummary> BuildPhaseSummaries(
        Roadmap? roadmap,
        IReadOnlyList<Ticket> leafTickets,
        HashSet<string> blockedTicketIds)
    {
        if (roadmap is null)
        {
            return [];
        }

        var summaries = new List<PhaseSummary>(roadmap.PhaseList.Count);
        foreach (var phase in roadmap.PhaseList)
        {
            var phaseTickets = leafTickets
                .Where(ticket => string.Equals(ticket.Phase, phase.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var complete = phaseTickets.Count(ticket => ticket.IsComplete);
            var inProgress = phaseTickets.Count(ticket => ticket.IsInProgress);
            var open = phaseTickets.Count(ticket => ticket.Status.Is(TicketStatus.Open));
            var blocked = phaseTickets.Count(ticket => ticket.Id is not null && blockedTicketIds.Contains(ticket.Id));

            var status = phaseTickets.Count switch
            {
                0 => PhaseStatus.NotStarted,
                var total when complete == total => PhaseStatus.Complete,
                _ when inProgress > 0 || complete > 0 => PhaseStatus.InProgress,
                _ => PhaseStatus.NotStarted,
            };

            summaries.Add(new PhaseSummary(
                phase,
                status,
                new TicketCounts(phaseTickets.Count, open, inProgress, complete, blocked)));
        }

        return summaries;
    }
}
