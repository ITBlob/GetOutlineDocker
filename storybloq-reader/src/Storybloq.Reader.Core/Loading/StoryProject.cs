using Storybloq.Reader.Core.Models;

namespace Storybloq.Reader.Core.Loading;

/// <summary>
/// An immutable snapshot of everything read from one <c>.story/</c> directory.
/// </summary>
/// <remarks>
/// Immutable so a reload can be published to the UI thread as a single reference swap: the
/// sidebar never renders a half-updated project.
/// </remarks>
public sealed class StoryProject
{
    public StoryProject(
        string root,
        StoryConfig? config,
        Roadmap? roadmap,
        IReadOnlyList<Ticket> tickets,
        IReadOnlyList<Issue> issues,
        IReadOnlyList<Note> notes,
        IReadOnlyList<Lesson> lessons,
        IReadOnlyList<HandoverFile> handovers,
        IReadOnlyList<LoadDiagnostic> diagnostics,
        DateTimeOffset loadedAt)
    {
        Root = root;
        Config = config;
        Roadmap = roadmap;
        Tickets = tickets;
        Issues = issues;
        Notes = notes;
        Lessons = lessons;
        Handovers = handovers;
        Diagnostics = diagnostics;
        LoadedAt = loadedAt;
    }

    public string Root { get; }

    public StoryConfig? Config { get; }

    public Roadmap? Roadmap { get; }

    public IReadOnlyList<Ticket> Tickets { get; }

    public IReadOnlyList<Issue> Issues { get; }

    public IReadOnlyList<Note> Notes { get; }

    public IReadOnlyList<Lesson> Lessons { get; }

    public IReadOnlyList<HandoverFile> Handovers { get; }

    public IReadOnlyList<LoadDiagnostic> Diagnostics { get; }

    public DateTimeOffset LoadedAt { get; }

    /// <summary>The project name from config, falling back to the directory name.</summary>
    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Config?.Project))
            {
                return Config.Project;
            }

            var name = Path.GetFileName(Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? Root : name;
        }
    }

    public StoryFeatures Features => Config?.EffectiveFeatures ?? StoryFeatures.AllEnabled;

    public bool HasErrors => Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);

    public static StoryProject Empty(string root) => new(
        root,
        config: null,
        roadmap: null,
        tickets: [],
        issues: [],
        notes: [],
        lessons: [],
        handovers: [],
        diagnostics: [],
        DateTimeOffset.UtcNow);
}
