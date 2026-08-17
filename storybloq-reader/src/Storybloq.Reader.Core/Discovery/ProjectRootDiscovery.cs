namespace Storybloq.Reader.Core.Discovery;

/// <summary>
/// Finds a project root the way the Storybloq CLI and MCP server do: walk up from a starting
/// directory to the nearest ancestor containing <c>.story/config.json</c>.
/// </summary>
/// <remarks>
/// This is what makes the folder picker forgiving — the user can point at any subdirectory of
/// their repository and still land on the project.
/// </remarks>
public static class ProjectRootDiscovery
{
    public const string EnvironmentVariable = "STORYBLOQ_PROJECT_ROOT";

    /// <summary>Retained by upstream for compatibility with the pre-rename tool.</summary>
    public const string LegacyEnvironmentVariable = "CLAUDESTORY_PROJECT_ROOT";

    /// <summary>
    /// Returns the project root, or <c>null</c> when no ancestor holds a <c>.story/config.json</c>.
    /// </summary>
    /// <param name="startDirectory">Where to start; defaults to the current directory.</param>
    /// <param name="environment">
    /// Environment lookup, injectable for tests. When either override variable is set it wins
    /// outright: an explicit root that turns out to be invalid yields <c>null</c> rather than
    /// silently walking up into a neighbouring project.
    /// </param>
    public static string? Discover(string? startDirectory = null, Func<string, string?>? environment = null)
    {
        environment ??= Environment.GetEnvironmentVariable;

        var overrideRoot = Coalesce(environment(EnvironmentVariable), environment(LegacyEnvironmentVariable));
        if (!string.IsNullOrWhiteSpace(overrideRoot))
        {
            var resolved = SafeFullPath(overrideRoot);
            return resolved is not null && IsProjectRoot(resolved) ? resolved : null;
        }

        var current = SafeFullPath(startDirectory ?? Directory.GetCurrentDirectory());
        while (current is not null)
        {
            if (IsProjectRoot(current))
            {
                return current;
            }

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.Ordinal))
            {
                break;
            }

            current = parent;
        }

        return null;
    }

    /// <summary>True when the directory directly contains <c>.story/config.json</c>.</summary>
    public static bool IsProjectRoot(string directory)
    {
        try
        {
            return File.Exists(StoryPaths.ConfigFile(directory));
        }
        catch (UnauthorizedAccessException)
        {
            // An unreadable directory is not a root we can use; keep walking rather than throw.
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private static string? Coalesce(string? first, string? second) =>
        string.IsNullOrWhiteSpace(first) ? second : first;

    private static string? SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException)
        {
            return null;
        }
    }
}
