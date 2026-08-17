namespace Storybloq.Reader.Core.Models;

/// <summary>
/// Wire values come from upstream <c>src/models/types.ts</c>. Every enum carries an
/// <see cref="Unknown"/> member at zero so a value written by a newer Storybloq release
/// deserializes instead of throwing — see <see cref="StoryEnum{TEnum}"/>.
/// </summary>
public enum TicketType
{
    Unknown = 0,
    Task,
    Feature,
    Chore,
}

public enum TicketStatus
{
    Unknown = 0,
    Open,
    InProgress,
    Complete,
}

public enum IssueStatus
{
    Unknown = 0,
    Open,
    InProgress,
    Resolved,
}

public enum IssueSeverity
{
    Unknown = 0,
    Critical,
    High,
    Medium,
    Low,
}

public enum NoteStatus
{
    Unknown = 0,
    Active,
    Archived,
}

public enum LessonStatus
{
    Unknown = 0,
    Active,
    Deprecated,
    Superseded,
}

public enum LessonSource
{
    Unknown = 0,
    Review,
    Correction,
    Postmortem,
    Manual,
}

public enum Lifecycle
{
    Unknown = 0,
    Active,
    Archived,
    Deleted,
}

/// <summary>Derived phase status. Never stored on disk — computed from leaf tickets.</summary>
public enum PhaseStatus
{
    NotStarted = 0,
    InProgress,
    Complete,
}
