using Storybloq.Reader.Core.Loading;

namespace Storybloq.Reader.Core.Watching;

public enum WatcherStatus
{
    /// <summary>Watching a live <c>.story</c> directory.</summary>
    Watching,

    /// <summary>The project root exists but has no <c>.story</c> yet; waiting for it to appear.</summary>
    AwaitingStoryDirectory,

    /// <summary>The root is gone — deleted, renamed, or on a disconnected drive.</summary>
    Unavailable,

    /// <summary>The platform watcher failed and could not be re-established.</summary>
    Faulted,
}

public sealed class StoryChangedEventArgs(StorySection sections) : EventArgs
{
    /// <summary>The sections needing a re-read.</summary>
    public StorySection Sections { get; } = sections;
}

public sealed class WatcherStatusEventArgs(WatcherStatus status, string? message = null) : EventArgs
{
    public WatcherStatus Status { get; } = status;

    public string? Message { get; } = message;
}

/// <summary>
/// Watches one project's <c>.story</c> directory.
/// </summary>
/// <remarks>
/// Declared in Core so the loading and view-model layers never reference a platform watcher
/// directly — which is what keeps every one of those layers testable off Windows.
/// </remarks>
public interface IStoryWatcher : IDisposable
{
    string ProjectRoot { get; }

    WatcherStatus Status { get; }

    /// <summary>Raised after coalescing, never once per raw filesystem event.</summary>
    event EventHandler<StoryChangedEventArgs>? Changed;

    event EventHandler<WatcherStatusEventArgs>? StatusChanged;

    void Start();
}
