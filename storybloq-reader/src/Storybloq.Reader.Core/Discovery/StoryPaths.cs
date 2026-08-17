namespace Storybloq.Reader.Core.Discovery;

/// <summary>
/// Layout of a <c>.story/</c> directory. Paths are built with <see cref="Path.Combine"/> so the
/// same Core code produces backslash paths on Windows and forward slashes elsewhere; nothing in
/// Core may assume a separator.
/// </summary>
public static class StoryPaths
{
    public const string StoryDirectoryName = ".story";
    public const string ConfigFileName = "config.json";
    public const string RoadmapFileName = "roadmap.json";
    public const string TicketsDirectoryName = "tickets";
    public const string IssuesDirectoryName = "issues";
    public const string NotesDirectoryName = "notes";
    public const string LessonsDirectoryName = "lessons";
    public const string HandoversDirectoryName = "handovers";

    /// <summary>Gitignored runtime churn. Watching these would produce event storms with nothing to show.</summary>
    public const string SnapshotsDirectoryName = "snapshots";
    public const string BusDirectoryName = "bus";

    public static string StoryDirectory(string projectRoot) => Path.Combine(projectRoot, StoryDirectoryName);

    public static string ConfigFile(string projectRoot) => Path.Combine(StoryDirectory(projectRoot), ConfigFileName);

    public static string RoadmapFile(string projectRoot) => Path.Combine(StoryDirectory(projectRoot), RoadmapFileName);

    public static string TicketsDirectory(string projectRoot) => Path.Combine(StoryDirectory(projectRoot), TicketsDirectoryName);

    public static string IssuesDirectory(string projectRoot) => Path.Combine(StoryDirectory(projectRoot), IssuesDirectoryName);

    public static string NotesDirectory(string projectRoot) => Path.Combine(StoryDirectory(projectRoot), NotesDirectoryName);

    public static string LessonsDirectory(string projectRoot) => Path.Combine(StoryDirectory(projectRoot), LessonsDirectoryName);

    public static string HandoversDirectory(string projectRoot) => Path.Combine(StoryDirectory(projectRoot), HandoversDirectoryName);
}
