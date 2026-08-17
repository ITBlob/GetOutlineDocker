namespace Storybloq.Reader.Core.Loading;

/// <summary>
/// The independently reloadable parts of a <c>.story/</c> directory. A file change dirties one
/// section, so a single ticket edit does not cost a re-read of every record in the project.
/// </summary>
[Flags]
public enum StorySection
{
    None = 0,
    Config = 1 << 0,
    Roadmap = 1 << 1,
    Tickets = 1 << 2,
    Issues = 1 << 3,
    Notes = 1 << 4,
    Lessons = 1 << 5,
    Handovers = 1 << 6,

    All = Config | Roadmap | Tickets | Issues | Notes | Lessons | Handovers,
}
