using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.State;
using Storybloq.Reader.Core.Watching;
using Storybloq.Reader.Platform.Watching;

namespace Storybloq.Reader.Platform.Projects;

public sealed class ProjectSessionUpdatedEventArgs(ProjectState state, StorySection sections) : EventArgs
{
    public ProjectState State { get; } = state;

    /// <summary>Which sections were re-read to produce this update.</summary>
    public StorySection Sections { get; } = sections;
}

/// <summary>
/// One project: its loaded snapshot, its derived state, and the watcher keeping both current.
/// </summary>
/// <remarks>
/// Reloads are serialised and publish a whole new <see cref="ProjectState"/> as a single
/// reference swap, so a consumer never observes a half-updated project. Events are raised on
/// whichever thread the watcher used — the UI layer is responsible for marshalling, which keeps
/// this class free of any dispatcher dependency and therefore testable off Windows.
///
/// Watching is deliberately reversible: the workspace retires watchers for projects the user has
/// not looked at recently, and must be able to bring them back. The watcher is therefore built
/// from a factory on demand rather than held for the session's lifetime.
/// </remarks>
public sealed class ProjectSession : IDisposable
{
    private readonly StoryProjectLoader _loader;
    private readonly Func<string, IStoryWatcher> _watcherFactory;
    private readonly object _reloadGate = new();
    private readonly object _watchGate = new();

    private volatile ProjectState _state;
    private IStoryWatcher? _watcher;
    private WatcherStatus? _lastStatus;
    private bool _hasWatchedBefore;
    private bool _disposed;

    public ProjectSession(
        string projectRoot,
        StoryProjectLoader? loader = null,
        Func<string, IStoryWatcher>? watcherFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        Root = Path.GetFullPath(projectRoot);
        _loader = loader ?? new StoryProjectLoader();
        _watcherFactory = watcherFactory ?? (root => new StoryFileSystemWatcher(root));
        _state = ProjectState.From(StoryProject.Empty(Root));
    }

    public string Root { get; }

    /// <summary>The current derived state. Always a complete, self-consistent snapshot.</summary>
    public ProjectState State => _state;

    public StoryProject Project => _state.Project;

    public WatcherStatus WatcherStatus
    {
        get
        {
            lock (_watchGate)
            {
                return _watcher?.Status ?? Core.Watching.WatcherStatus.Unavailable;
            }
        }
    }

    public bool IsWatching
    {
        get
        {
            lock (_watchGate)
            {
                return _watcher is not null;
            }
        }
    }

    public event EventHandler<ProjectSessionUpdatedEventArgs>? Updated;

    public event EventHandler<WatcherStatusEventArgs>? StatusChanged;

    /// <summary>Performs the initial load, then begins watching.</summary>
    public void Start()
    {
        Reload(StorySection.All);
        StartWatching();
    }

    public void StartWatching()
    {
        IStoryWatcher watcher;
        bool isRearm;

        lock (_watchGate)
        {
            if (_disposed || _watcher is not null)
            {
                return;
            }

            watcher = _watcherFactory(Root);
            watcher.Changed += OnWatcherChanged;
            watcher.StatusChanged += OnWatcherStatusChanged;

            _watcher = watcher;
            _lastStatus = null;
            isRearm = _hasWatchedBefore;
            _hasWatchedBefore = true;
        }

        // Anything that happened while this project was unwatched was missed by definition.
        if (isRearm)
        {
            Reload(StorySection.All);
        }

        watcher.Start();
    }

    /// <summary>
    /// Stops watching while keeping the loaded snapshot. The session stays usable and can be
    /// watched again later — unlike <see cref="Dispose"/>.
    /// </summary>
    public void StopWatching()
    {
        IStoryWatcher? watcher;

        lock (_watchGate)
        {
            watcher = _watcher;
            _watcher = null;
            _lastStatus = null;
        }

        if (watcher is null)
        {
            return;
        }

        watcher.Changed -= OnWatcherChanged;
        watcher.StatusChanged -= OnWatcherStatusChanged;
        watcher.Dispose();
    }

    /// <summary>Re-reads everything, regardless of what the watcher has seen.</summary>
    public void Refresh() => Reload(StorySection.All);

    private void OnWatcherChanged(object? sender, StoryChangedEventArgs e) => Reload(e.Sections);

    private void OnWatcherStatusChanged(object? sender, WatcherStatusEventArgs e)
    {
        // Recovering a lost watch means the project may have changed entirely while we were
        // blind to it. The first arm is excluded: the caller has just loaded, and reloading
        // again would double the cold-start cost of every project.
        bool recovered;

        lock (_watchGate)
        {
            recovered = e.Status == Core.Watching.WatcherStatus.Watching
                && _lastStatus is not null
                && _lastStatus != Core.Watching.WatcherStatus.Watching;

            _lastStatus = e.Status;
        }

        if (recovered)
        {
            Reload(StorySection.All);
        }

        StatusChanged?.Invoke(this, e);
    }

    private void Reload(StorySection sections)
    {
        if (_disposed || sections == StorySection.None)
        {
            return;
        }

        ProjectState state;

        // Serialised so two overlapping flushes cannot interleave their reads.
        lock (_reloadGate)
        {
            if (_disposed)
            {
                return;
            }

            var project = _loader.Reload(_state.Project, sections);
            state = ProjectState.From(project);
            _state = state;
        }

        Updated?.Invoke(this, new ProjectSessionUpdatedEventArgs(state, sections));
    }

    public void Dispose()
    {
        lock (_reloadGate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        StopWatching();
    }
}
