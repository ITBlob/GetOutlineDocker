using Microsoft.Extensions.Time.Testing;
using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.Watching;
using Xunit;

namespace Storybloq.Reader.Core.Tests;

public class ChangeCoalescerTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(1);

    private static ChangeCoalescer Create(
        List<StorySection> flushes,
        FakeTimeProvider time,
        int threshold = 200) =>
        new(flushes.Add, Quiet, MaxDelay, threshold, time);

    [Fact]
    public void ABurstOfEventsProducesOneFlushNamingEverySectionTouched()
    {
        var flushes = new List<StorySection>();
        var time = new FakeTimeProvider();
        using var coalescer = Create(flushes, time);

        // What one `storybloq ticket update` actually looks like from the outside.
        coalescer.Record("tickets/T-004.json.tmp");
        coalescer.Record("tickets/T-004.json");
        coalescer.Record("tickets");
        coalescer.Record("issues/ISS-001.json");

        Assert.Empty(flushes);

        time.Advance(Quiet);

        var sections = Assert.Single(flushes);
        Assert.Equal(StorySection.Tickets | StorySection.Issues, sections);
    }

    [Fact]
    public void TheQuietWindowRestartsWhileEventsKeepArriving()
    {
        var flushes = new List<StorySection>();
        var time = new FakeTimeProvider();
        using var coalescer = Create(flushes, time);

        coalescer.Record("tickets/T-001.json");
        time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Empty(flushes);

        coalescer.Record("tickets/T-002.json");
        time.Advance(TimeSpan.FromMilliseconds(150));
        Assert.Empty(flushes);

        time.Advance(TimeSpan.FromMilliseconds(60));
        Assert.Single(flushes);
    }

    /// <summary>
    /// A `git checkout` across a large .story/ delivers a continuous stream. A debounce that only
    /// resets would never fire, leaving the sidebar stale for the whole operation.
    /// </summary>
    [Fact]
    public void AContinuousStreamStillFlushesAtTheHardDeadline()
    {
        var flushes = new List<StorySection>();
        var time = new FakeTimeProvider();
        using var coalescer = Create(flushes, time);

        for (var elapsed = TimeSpan.Zero; elapsed < TimeSpan.FromSeconds(2); elapsed += TimeSpan.FromMilliseconds(50))
        {
            coalescer.Record("tickets/T-001.json");
            time.Advance(TimeSpan.FromMilliseconds(50));
        }

        // Two full MaxDelay windows elapsed, so the stream was flushed rather than starved.
        Assert.NotEmpty(flushes);
        Assert.All(flushes, sections => Assert.Equal(StorySection.Tickets, sections));
    }

    [Fact]
    public void EnoughEventsEscalateToAFullReload()
    {
        var flushes = new List<StorySection>();
        var time = new FakeTimeProvider();
        using var coalescer = Create(flushes, time, threshold: 5);

        for (var i = 0; i < 6; i++)
        {
            coalescer.Record($"tickets/T-{i:000}.json");
        }

        time.Advance(Quiet);

        Assert.Equal(StorySection.All, Assert.Single(flushes));
    }

    [Fact]
    public void IgnoredPathsNeitherQueueNorSchedule()
    {
        var flushes = new List<StorySection>();
        var time = new FakeTimeProvider();
        using var coalescer = Create(flushes, time);

        Assert.False(coalescer.Record("snapshots/latest.json"));
        Assert.False(coalescer.Record("bus/mailbox.json"));
        Assert.False(coalescer.Record("tickets/T-001.json.tmp"));

        Assert.Equal(StorySection.None, coalescer.Pending);

        time.Advance(MaxDelay + Quiet);
        Assert.Empty(flushes);
    }

    [Fact]
    public void NothingPendingMeansNothingFlushed()
    {
        var flushes = new List<StorySection>();
        var time = new FakeTimeProvider();
        using var coalescer = Create(flushes, time);

        time.Advance(TimeSpan.FromMinutes(5));
        coalescer.FlushNow();

        Assert.Empty(flushes);
    }

    [Fact]
    public void FlushNowBypassesTheQuietWindow()
    {
        var flushes = new List<StorySection>();
        var time = new FakeTimeProvider();
        using var coalescer = Create(flushes, time);

        coalescer.Record("notes/N-001.json");
        coalescer.FlushNow();

        Assert.Equal(StorySection.Notes, Assert.Single(flushes));

        // The pending set was consumed, so the timer has nothing left to deliver.
        time.Advance(MaxDelay + Quiet);
        Assert.Single(flushes);
    }

    [Fact]
    public void AFailedWatcherCanDemandAFullReload()
    {
        var flushes = new List<StorySection>();
        var time = new FakeTimeProvider();
        using var coalescer = Create(flushes, time);

        coalescer.RequestFullReload();
        time.Advance(Quiet);

        Assert.Equal(StorySection.All, Assert.Single(flushes));
    }

    [Fact]
    public void SuccessiveBurstsFlushIndependently()
    {
        var flushes = new List<StorySection>();
        var time = new FakeTimeProvider();
        using var coalescer = Create(flushes, time);

        coalescer.Record("tickets/T-001.json");
        time.Advance(Quiet);

        coalescer.Record("lessons/L-001.json");
        time.Advance(Quiet);

        Assert.Equal([StorySection.Tickets, StorySection.Lessons], flushes);
    }

    [Fact]
    public void DisposingStopsFurtherFlushes()
    {
        var flushes = new List<StorySection>();
        var time = new FakeTimeProvider();
        var coalescer = Create(flushes, time);

        coalescer.Record("tickets/T-001.json");
        coalescer.Dispose();

        time.Advance(MaxDelay + Quiet);
        Assert.Empty(flushes);
    }
}
