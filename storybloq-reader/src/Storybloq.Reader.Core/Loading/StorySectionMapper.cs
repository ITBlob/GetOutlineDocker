using Storybloq.Reader.Core.Discovery;

namespace Storybloq.Reader.Core.Loading;

/// <summary>
/// Maps a path inside <c>.story/</c> onto the section it dirties.
/// </summary>
/// <remarks>
/// Path comparison is ordinal-ignore-case and separator-agnostic. Windows hands back
/// backslash-separated, arbitrarily-cased paths from <c>FileSystemWatcher</c>, and Core must
/// classify them identically to the forward-slash paths tests feed it.
/// </remarks>
public static class StorySectionMapper
{
    private static readonly char[] Separators = ['/', '\\'];

    /// <summary>
    /// Suffixes that mean "a writer is mid-flight". Upstream writes temp-then-rename, so these
    /// appear and vanish constantly and must never trigger a reload.
    /// </summary>
    private static readonly string[] IgnoredSuffixes = [".tmp", ".temp", ".swp", ".swx", "~", ".lock"];

    /// <param name="relativePath">A path relative to the <c>.story</c> directory itself.</param>
    /// <returns>The dirtied section, or <see cref="StorySection.None"/> when the path is noise.</returns>
    public static StorySection Map(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return StorySection.None;
        }

        var segments = relativePath.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return StorySection.None;
        }

        // Ignore "." and ".." noise, plus dotfiles anywhere in the path.
        foreach (var segment in segments)
        {
            if (segment is "." or "..")
            {
                return StorySection.None;
            }
        }

        var last = segments[^1];
        if (last.StartsWith('.'))
        {
            return StorySection.None;
        }

        foreach (var suffix in IgnoredSuffixes)
        {
            if (last.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                return StorySection.None;
            }
        }

        var head = segments[0];

        if (segments.Length == 1)
        {
            if (Equals(head, StoryPaths.ConfigFileName))
            {
                return StorySection.Config;
            }

            if (Equals(head, StoryPaths.RoadmapFileName))
            {
                return StorySection.Roadmap;
            }
        }

        return head switch
        {
            _ when Equals(head, StoryPaths.TicketsDirectoryName) => StorySection.Tickets,
            _ when Equals(head, StoryPaths.IssuesDirectoryName) => StorySection.Issues,
            _ when Equals(head, StoryPaths.NotesDirectoryName) => StorySection.Notes,
            _ when Equals(head, StoryPaths.LessonsDirectoryName) => StorySection.Lessons,
            _ when Equals(head, StoryPaths.HandoversDirectoryName) => StorySection.Handovers,

            // snapshots/ and bus/ are gitignored runtime state: high churn, nothing to display.
            _ => StorySection.None,
        };
    }

    private static bool Equals(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
