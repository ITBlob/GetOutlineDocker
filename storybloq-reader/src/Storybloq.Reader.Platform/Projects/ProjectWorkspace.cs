using Storybloq.Reader.Core.Discovery;
using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.State;

namespace Storybloq.Reader.Platform.Projects;

public enum AddProjectOutcome
{
    Added,
    AlreadyPresent,

    /// <summary>Neither the chosen directory nor any ancestor holds a <c>.story/config.json</c>.</summary>
    NotAStorybloqProject,
}

public sealed record AddProjectResult(AddProjectOutcome Outcome, ProjectSession? Session, string? Message);

/// <summary>
/// The set of projects the user is watching, and the cross-project roll-up over them.
/// </summary>
/// <remarks>
/// Watching every project at once would be wasteful for someone tracking a dozen repositories,
/// so only <see cref="MaxWatchedProjects"/> are actively watched; the least recently used watcher
/// is stopped when a project outside that set is selected. Unwatched projects keep their last
/// loaded snapshot and refresh on selection, so they are stale but never wrong-looking — the UI
/// shows which ones are live.
/// </remarks>
public sealed class ProjectWorkspace : IDisposable
{
    private readonly ProjectRegistryStore _store;
    private readonly Func<string, ProjectSession> _sessionFactory;
    private readonly List<ProjectSession> _sessions = [];
    private readonly List<string> _watchOrder = [];
    private readonly object _gate = new();

    private bool _disposed;

    public ProjectWorkspace(
        ProjectRegistryStore? store = null,
        Func<string, ProjectSession>? sessionFactory = null,
        int maxWatchedProjects = 12)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxWatchedProjects);

        _store = store ?? new ProjectRegistryStore();
        _sessionFactory = sessionFactory ?? (root => new ProjectSession(root));
        MaxWatchedProjects = maxWatchedProjects;
    }

    public int MaxWatchedProjects { get; }

    public IReadOnlyList<ProjectSession> Sessions
    {
        get
        {
            lock (_gate)
            {
                return _sessions.ToList();
            }
        }
    }

    /// <summary>Raised when a project is added or removed — not when one merely reloads.</summary>
    public event EventHandler? ProjectsChanged;

    /// <summary>Raised whenever any project's state changes, for the aggregate views.</summary>
    public event EventHandler<ProjectSessionUpdatedEventArgs>? ProjectUpdated;

    /// <summary>Everything demanding attention across every open project, worst first.</summary>
    public IReadOnlyList<AttentionItem> Attention =>
        AttentionQuery.Collect(Sessions.Select(session => session.State));

    /// <summary>Restores the saved project list. Roots that no longer resolve are kept.</summary>
    public void LoadSaved()
    {
        foreach (var registered in _store.Load())
        {
            // Kept even when currently unreachable: an unplugged drive should not silently
            // erase the user's project list.
            AddCore(registered.Root, discover: false);
        }

        ProjectsChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Adds a project from any directory inside it, walking up to the real root the same way
    /// the CLI does.
    /// </summary>
    public AddProjectResult Add(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return new AddProjectResult(AddProjectOutcome.NotAStorybloqProject, null, "No directory chosen.");
        }

        var root = ProjectRootDiscovery.Discover(directory, _ => null);
        if (root is null)
        {
            return new AddProjectResult(
                AddProjectOutcome.NotAStorybloqProject,
                null,
                $"No {StoryPaths.StoryDirectoryName}/{StoryPaths.ConfigFileName} found in {directory} or any parent directory.");
        }

        var result = AddCore(root, discover: true);
        if (result.Outcome == AddProjectOutcome.Added)
        {
            Save();
            ProjectsChanged?.Invoke(this, EventArgs.Empty);
        }

        return result;
    }

    private AddProjectResult AddCore(string root, bool discover)
    {
        ProjectSession session;

        lock (_gate)
        {
            var full = Path.GetFullPath(root);
            var existing = _sessions.FirstOrDefault(candidate => PathsEqual(candidate.Root, full));
            if (existing is not null)
            {
                return new AddProjectResult(AddProjectOutcome.AlreadyPresent, existing, null);
            }

            session = _sessionFactory(full);
            session.Updated += OnSessionUpdated;
            _sessions.Add(session);
        }

        session.Start();
        Touch(session.Root);

        return new AddProjectResult(AddProjectOutcome.Added, session, null);
    }

    public bool Remove(string root)
    {
        ProjectSession? session;

        lock (_gate)
        {
            session = _sessions.FirstOrDefault(candidate => PathsEqual(candidate.Root, root));
            if (session is null)
            {
                return false;
            }

            _sessions.Remove(session);
            _watchOrder.RemoveAll(entry => PathsEqual(entry, session.Root));
        }

        session.Updated -= OnSessionUpdated;
        session.Dispose();

        Save();
        ProjectsChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Marks a project as most recently used, starting its watcher and retiring the oldest one
    /// if that pushes us over the cap.
    /// </summary>
    public void Touch(string root)
    {
        ProjectSession? toStart = null;
        var toStop = new List<ProjectSession>();

        lock (_gate)
        {
            var session = _sessions.FirstOrDefault(candidate => PathsEqual(candidate.Root, root));
            if (session is null)
            {
                return;
            }

            _watchOrder.RemoveAll(entry => PathsEqual(entry, session.Root));
            _watchOrder.Insert(0, session.Root);

            if (!session.IsWatching)
            {
                toStart = session;
            }

            foreach (var stale in _watchOrder.Skip(MaxWatchedProjects))
            {
                var staleSession = _sessions.FirstOrDefault(candidate => PathsEqual(candidate.Root, stale));
                if (staleSession is { IsWatching: true })
                {
                    toStop.Add(staleSession);
                }
            }
        }

        toStart?.StartWatching();

        foreach (var session in toStop)
        {
            // Retiring a watcher keeps the session and its snapshot intact — the project is
            // still listed and readable, just no longer live. Disposing here would make it
            // impossible to bring back when the user selects it again.
            session.StopWatching();
        }
    }

    public void Save() =>
        _store.Save(Sessions.Select(session => new RegisteredProject { Root = session.Root }));

    private void OnSessionUpdated(object? sender, ProjectSessionUpdatedEventArgs e) =>
        ProjectUpdated?.Invoke(this, e);

    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        List<ProjectSession> sessions;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            sessions = _sessions.ToList();
            _sessions.Clear();
            _watchOrder.Clear();
        }

        foreach (var session in sessions)
        {
            session.Updated -= OnSessionUpdated;
            session.Dispose();
        }
    }
}
