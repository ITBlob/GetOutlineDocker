using Storybloq.Reader.Core.Discovery;
using Storybloq.Reader.Core.Watching;

namespace Storybloq.Reader.Platform.Watching;

/// <summary>
/// Watches a project's <c>.story</c> directory with <see cref="FileSystemWatcher"/>, and keeps
/// watching it across the many ways that watch can break.
/// </summary>
/// <remarks>
/// A naive <c>FileSystemWatcher</c> is fine in a demo and unusable in practice. The failure modes
/// this class exists to absorb, all of which occur during ordinary agent-assisted work:
/// <list type="bullet">
///   <item><b>Buffer overflow.</b> Windows delivers change notifications through a fixed kernel
///   buffer. A <c>git checkout</c> that rewrites a large <c>.story/</c> overruns it, the
///   <see cref="FileSystemWatcher.Error"/> event fires, and every event in that window is simply
///   lost. A watcher that ignores <c>Error</c> looks healthy while showing stale data forever —
///   the single worst outcome for a live sidebar. Here it forces a full reload and re-arms.</item>
///   <item><b>The directory arriving late.</b> A user adds a repository before running
///   <c>storybloq init</c>. Rather than failing, the watcher waits on the parent for
///   <c>.story</c> to appear and promotes itself when it does.</item>
///   <item><b>The directory going away.</b> Branch switches, cleanups, and disconnected network
///   drives all delete the watched path out from under the handle. The watcher degrades to
///   <see cref="WatcherStatus.Unavailable"/> and keeps retrying instead of throwing.</item>
/// </list>
/// Raw events are never surfaced directly: they go through a <see cref="ChangeCoalescer"/> so one
/// logical edit produces one reload.
/// </remarks>
public sealed class StoryFileSystemWatcher : IStoryWatcher
{
    /// <summary>The largest buffer Windows accepts. Bigger buffer, fewer overflows.</summary>
    public const int MaxInternalBufferSize = 64 * 1024;

    private static readonly TimeSpan DefaultRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FaultRetryInterval = TimeSpan.FromMilliseconds(500);

    private readonly string _storyDirectory;
    private readonly ChangeCoalescer _coalescer;
    private readonly ITimer _healthTimer;
    private readonly TimeSpan _retryInterval;
    private readonly object _gate = new();

    private FileSystemWatcher? _storyWatcher;
    private FileSystemWatcher? _parentWatcher;
    private WatcherStatus _status = WatcherStatus.Unavailable;
    private bool _started;
    private bool _disposed;
    private bool _hasArmedOnce;

    public StoryFileSystemWatcher(
        string projectRoot,
        TimeSpan? quietWindow = null,
        TimeSpan? maxDelay = null,
        TimeSpan? retryInterval = null,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectRoot);

        ProjectRoot = Path.GetFullPath(projectRoot);
        _storyDirectory = StoryPaths.StoryDirectory(ProjectRoot);
        _retryInterval = retryInterval ?? DefaultRetryInterval;

        var provider = timeProvider ?? TimeProvider.System;
        _coalescer = new ChangeCoalescer(OnCoalescedChange, quietWindow, maxDelay, timeProvider: provider);
        _healthTimer = provider.CreateTimer(_ => CheckHealth(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public string ProjectRoot { get; }

    public WatcherStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public event EventHandler<StoryChangedEventArgs>? Changed;

    public event EventHandler<WatcherStatusEventArgs>? StatusChanged;

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _started)
            {
                return;
            }

            _started = true;

            // A periodic liveness check, running for the lifetime of the watcher rather than
            // only while recovering. It is not belt-and-braces: when the watched directory is
            // deleted, neither Windows nor inotify reliably delivers an event naming the
            // directory itself, so an event-driven watcher simply goes quiet while appearing
            // healthy. Polling for existence is the only way to notice.
            _healthTimer.Change(_retryInterval, _retryInterval);
        }

        Rearm();
    }

    /// <summary>Cheap existence check; a full re-arm only happens when something is actually wrong.</summary>
    private void CheckHealth()
    {
        lock (_gate)
        {
            if (_disposed || !_started)
            {
                return;
            }

            if (_status == WatcherStatus.Watching && Directory.Exists(_storyDirectory))
            {
                return;
            }
        }

        Rearm();
    }

    /// <summary>Re-establishes the watch, whatever state it is currently in.</summary>
    private void Rearm()
    {
        var requestFullReload = false;
        WatcherStatus status;
        string? message = null;

        lock (_gate)
        {
            if (_disposed || !_started)
            {
                return;
            }

            DisposeWatchers();

            if (!Directory.Exists(ProjectRoot))
            {
                status = WatcherStatus.Unavailable;
                message = $"{ProjectRoot} is not available.";
                ScheduleHealthCheck(_retryInterval);
            }
            else if (!Directory.Exists(_storyDirectory))
            {
                status = TryWatchParentForStoryDirectory(out message)
                    ? WatcherStatus.AwaitingStoryDirectory
                    : WatcherStatus.Faulted;

                // Watching the parent is best-effort; the poll is what guarantees recovery.
                ScheduleHealthCheck(_retryInterval);
            }
            else if (TryWatchStoryDirectory(out message))
            {
                status = WatcherStatus.Watching;
                ScheduleHealthCheck(_retryInterval);

                // Anything that happened while unarmed was missed by definition.
                requestFullReload = _hasArmedOnce;
                _hasArmedOnce = true;
            }
            else
            {
                status = WatcherStatus.Faulted;
                ScheduleHealthCheck(_retryInterval);
            }

            _status = status;
        }

        StatusChanged?.Invoke(this, new WatcherStatusEventArgs(status, message));

        if (requestFullReload)
        {
            _coalescer.RequestFullReload();
        }
    }

    private bool TryWatchStoryDirectory(out string? message)
    {
        try
        {
            var watcher = new FileSystemWatcher(_storyDirectory)
            {
                IncludeSubdirectories = true,
                InternalBufferSize = MaxInternalBufferSize,
                NotifyFilter = NotifyFilters.FileName
                    | NotifyFilters.DirectoryName
                    | NotifyFilters.LastWrite
                    | NotifyFilters.Size,
            };

            watcher.Created += OnChanged;
            watcher.Changed += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;

            _storyWatcher = watcher;
            message = null;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            message = exception.Message;
            return false;
        }
    }

    /// <summary>
    /// Waits for <c>.story</c> to be created, so a project added before <c>storybloq init</c>
    /// starts working the moment it is initialised.
    /// </summary>
    private bool TryWatchParentForStoryDirectory(out string? message)
    {
        try
        {
            var watcher = new FileSystemWatcher(ProjectRoot)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName,
                Filter = StoryPaths.StoryDirectoryName,
            };

            watcher.Created += (_, _) => Rearm();
            watcher.Renamed += (_, _) => Rearm();
            watcher.Error += (_, _) => ScheduleHealthCheck(FaultRetryInterval);
            watcher.EnableRaisingEvents = true;

            _parentWatcher = watcher;
            message = $"Waiting for {StoryPaths.StoryDirectoryName} to appear in {ProjectRoot}.";
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            message = exception.Message;
            return false;
        }
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => Record(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        // Atomic writes land as a rename, so both ends of it matter: the old name may be the
        // record that just disappeared, the new one the record that replaced it.
        Record(e.OldFullPath);
        Record(e.FullPath);
    }

    private void Record(string fullPath)
    {
        // The .story directory itself was removed or replaced; the handle is now meaningless.
        if (PathsEqual(fullPath, _storyDirectory))
        {
            ScheduleHealthCheck(TimeSpan.Zero);
            return;
        }

        _coalescer.Record(GetRelativePath(fullPath));
    }

    private string? GetRelativePath(string fullPath)
    {
        try
        {
            return Path.GetRelativePath(_storyDirectory, fullPath);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The overflow path. Events were dropped, so the only honest recovery is to re-read
    /// everything and rebuild the watch.
    /// </summary>
    private void OnError(object sender, ErrorEventArgs e)
    {
        WatcherStatus status;

        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _status = WatcherStatus.Faulted;
            status = _status;
            ScheduleHealthCheck(FaultRetryInterval);
        }

        StatusChanged?.Invoke(this, new WatcherStatusEventArgs(status, e.GetException()?.Message));
        _coalescer.RequestFullReload();
    }

    private void OnCoalescedChange(Core.Loading.StorySection sections) =>
        Changed?.Invoke(this, new StoryChangedEventArgs(sections));

    /// <summary>
    /// Runs the next health check after <paramref name="delay"/>, then keeps polling at the
    /// normal interval. The periodic tail is what makes recovery automatic no matter which of
    /// the failure modes occurred.
    /// </summary>
    private void ScheduleHealthCheck(TimeSpan delay)
    {
        if (!_disposed)
        {
            _healthTimer.Change(delay, _retryInterval);
        }
    }

    private void DisposeWatchers()
    {
        DisposeWatcher(ref _storyWatcher);
        DisposeWatcher(ref _parentWatcher);
    }

    private static void DisposeWatcher(ref FileSystemWatcher? watcher)
    {
        if (watcher is null)
        {
            return;
        }

        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }
        catch (Exception exception) when (exception is IOException or ObjectDisposedException)
        {
            // Tearing down a watcher whose directory already vanished is expected.
        }
        finally
        {
            watcher = null;
        }
    }

    /// <summary>Windows paths differ only by case and separator far more often than by content.</summary>
    private static bool PathsEqual(string left, string right) =>
        string.Equals(
            left.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            right.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeWatchers();
        }

        _healthTimer.Dispose();
        _coalescer.Dispose();
    }
}
