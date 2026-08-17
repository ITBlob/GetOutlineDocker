using Storybloq.Reader.Core.Loading;

namespace Storybloq.Reader.Core.Watching;

/// <summary>
/// Turns a storm of filesystem events into at most one reload per quiet period, and reports
/// which sections actually need re-reading.
/// </summary>
/// <remarks>
/// Three separate problems are solved here, all of which show up the first time a real project
/// is watched:
/// <list type="bullet">
///   <item><b>Write amplification.</b> A single <c>storybloq ticket update</c> produces several
///   events (temp create, rename, directory touch). Reloading on each one would re-read the
///   project three times for one logical change.</item>
///   <item><b>Starvation.</b> A pure reset-on-every-event debounce never fires while events keep
///   arriving — and a <c>git checkout</c> across a large <c>.story/</c> arrives as a continuous
///   stream. <see cref="MaxDelay"/> guarantees a flush even mid-storm.</item>
///   <item><b>Cost.</b> Above <see cref="FullReloadThreshold"/> events, tracking individual
///   sections stops paying for itself and the coalescer escalates to a full reload.</item>
/// </list>
/// </remarks>
public sealed class ChangeCoalescer : IDisposable
{
    public static TimeSpan DefaultQuietWindow => TimeSpan.FromMilliseconds(200);

    public static TimeSpan DefaultMaxDelay => TimeSpan.FromSeconds(1);

    private readonly Action<StorySection> _onFlush;
    private readonly TimeProvider _timeProvider;
    private readonly ITimer _timer;
    private readonly object _gate = new();

    private StorySection _pending = StorySection.None;
    private DateTimeOffset? _firstPendingAt;
    private DateTimeOffset _lastEventAt;
    private int _eventCount;
    private bool _disposed;

    public ChangeCoalescer(
        Action<StorySection> onFlush,
        TimeSpan? quietWindow = null,
        TimeSpan? maxDelay = null,
        int fullReloadThreshold = 200,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(onFlush);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(fullReloadThreshold);

        _onFlush = onFlush;
        _timeProvider = timeProvider ?? TimeProvider.System;
        QuietWindow = quietWindow ?? DefaultQuietWindow;
        MaxDelay = maxDelay ?? DefaultMaxDelay;
        FullReloadThreshold = fullReloadThreshold;

        _timer = _timeProvider.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>How long the directory must be quiet before a flush.</summary>
    public TimeSpan QuietWindow { get; }

    /// <summary>Upper bound between the first pending event and its flush.</summary>
    public TimeSpan MaxDelay { get; }

    public int FullReloadThreshold { get; }

    /// <summary>Sections awaiting a flush. Exposed for tests and diagnostics.</summary>
    public StorySection Pending
    {
        get
        {
            lock (_gate)
            {
                return _pending;
            }
        }
    }

    /// <summary>Records a change to a path relative to the <c>.story</c> directory.</summary>
    /// <returns>True when the path mapped to a section and was queued.</returns>
    public bool Record(string? relativePath) => RecordSection(StorySectionMapper.Map(relativePath));

    /// <summary>Records a change to a known section, bypassing path mapping.</summary>
    public bool RecordSection(StorySection section)
    {
        if (section == StorySection.None)
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            var now = _timeProvider.GetUtcNow();
            _pending |= section;
            _firstPendingAt ??= now;
            _lastEventAt = now;

            if (++_eventCount > FullReloadThreshold)
            {
                _pending = StorySection.All;
            }

            _timer.Change(ComputeDelay(now), Timeout.InfiniteTimeSpan);
            return true;
        }
    }

    /// <summary>
    /// Escalates straight to a full reload — used when the watcher itself failed and the event
    /// stream can no longer be trusted.
    /// </summary>
    public void RequestFullReload() => RecordSection(StorySection.All);

    /// <summary>Flushes immediately if anything is pending, ignoring the quiet window.</summary>
    public void FlushNow()
    {
        StorySection sections;
        lock (_gate)
        {
            sections = TakePending();
        }

        if (sections != StorySection.None)
        {
            _onFlush(sections);
        }
    }

    private TimeSpan ComputeDelay(DateTimeOffset now)
    {
        var quietDeadline = now + QuietWindow;
        var hardDeadline = (_firstPendingAt ?? now) + MaxDelay;
        var deadline = quietDeadline < hardDeadline ? quietDeadline : hardDeadline;

        var delay = deadline - now;
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private void OnTimer()
    {
        StorySection sections;

        lock (_gate)
        {
            if (_disposed || _pending == StorySection.None)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            var quietElapsed = now - _lastEventAt >= QuietWindow;
            var hardDeadlineReached = _firstPendingAt is { } first && now - first >= MaxDelay;

            if (!quietElapsed && !hardDeadlineReached)
            {
                // An event landed between the timer being scheduled and this callback running.
                _timer.Change(ComputeDelay(now), Timeout.InfiniteTimeSpan);
                return;
            }

            sections = TakePending();
        }

        if (sections != StorySection.None)
        {
            _onFlush(sections);
        }
    }

    private StorySection TakePending()
    {
        var sections = _pending;
        _pending = StorySection.None;
        _firstPendingAt = null;
        _eventCount = 0;
        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return sections;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Dispose();
    }
}
