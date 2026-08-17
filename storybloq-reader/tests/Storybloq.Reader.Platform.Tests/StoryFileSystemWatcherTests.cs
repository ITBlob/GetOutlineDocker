using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.Watching;
using Storybloq.Reader.Platform.Watching;
using Xunit;

namespace Storybloq.Reader.Platform.Tests;

/// <summary>
/// Integration tests against a real filesystem watcher. They run on whatever OS the suite runs
/// on — inotify here, ReadDirectoryChangesW on the Windows CI job — which is the point: the
/// same assertions have to hold on both.
/// </summary>
[Collection("filesystem")]
public class StoryFileSystemWatcherTests
{
    private static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(50);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Retry = TimeSpan.FromMilliseconds(100);

    private static StoryFileSystemWatcher Create(string root) => new(root, Quiet, MaxDelay, Retry);

    [Fact]
    public void ReportsWatchingOnceStartedOverAnInitialisedProject()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");

        using var watcher = Create(root);
        watcher.Start();

        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));
    }

    [Fact]
    public void WritingATicketRaisesAChangeForTheTicketsSection()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");
        var sections = new List<StorySection>();

        using var watcher = Create(root);
        watcher.Changed += (_, e) => sections.Add(e.Sections);
        watcher.Start();
        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));

        TempDirectory.WriteTicket(root, "T-001");

        Assert.True(Wait.Until(() => sections.Count > 0));
        Assert.All(sections, section => Assert.True(section.HasFlag(StorySection.Tickets)));
    }

    [Fact]
    public void ABurstOfWritesCollapsesIntoFarFewerReloadsThanFiles()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");
        var flushes = 0;

        using var watcher = Create(root);
        watcher.Changed += (_, _) => Interlocked.Increment(ref flushes);
        watcher.Start();
        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));

        for (var i = 0; i < 40; i++)
        {
            TempDirectory.WriteTicket(root, $"T-{i:000}");
        }

        Assert.True(Wait.Until(() => Volatile.Read(ref flushes) > 0));
        Thread.Sleep(400);

        // 40 files, each producing multiple raw events; without coalescing this would be
        // dozens of reloads.
        Assert.InRange(Volatile.Read(ref flushes), 1, 10);
    }

    [Fact]
    public void ChangesUnderSnapshotsAndBusNeverWakeTheReader()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");
        var flushes = 0;

        using var watcher = Create(root);
        watcher.Changed += (_, _) => Interlocked.Increment(ref flushes);
        watcher.Start();
        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));

        var story = Path.Combine(root, ".story");
        Directory.CreateDirectory(Path.Combine(story, "snapshots"));
        Directory.CreateDirectory(Path.Combine(story, "bus"));

        for (var i = 0; i < 10; i++)
        {
            File.WriteAllText(Path.Combine(story, "snapshots", $"snap-{i}.json"), "{}");
            File.WriteAllText(Path.Combine(story, "bus", $"msg-{i}.json"), "{}");
        }

        Thread.Sleep(500);
        Assert.Equal(0, Volatile.Read(ref flushes));

        // ...and a real change still gets through afterwards.
        TempDirectory.WriteTicket(root, "T-001");
        Assert.True(Wait.Until(() => Volatile.Read(ref flushes) > 0));
    }

    [Fact]
    public void TemporaryWriteArtefactsDoNotTriggerReloads()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");
        var flushes = 0;

        using var watcher = Create(root);
        watcher.Changed += (_, _) => Interlocked.Increment(ref flushes);
        watcher.Start();
        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));

        var tickets = Path.Combine(root, ".story", "tickets");
        File.WriteAllText(Path.Combine(tickets, "T-001.json.tmp"), "{}");

        Thread.Sleep(400);
        Assert.Equal(0, Volatile.Read(ref flushes));
    }

    /// <summary>
    /// Someone adds a repository, then runs <c>storybloq init</c> afterwards. The reader has to
    /// pick that up on its own.
    /// </summary>
    [Fact]
    public void AProjectInitialisedAfterTheFactIsPickedUpAutomatically()
    {
        using var temp = new TempDirectory();
        var root = temp.Combine("not-yet");
        Directory.CreateDirectory(root);

        using var watcher = Create(root);
        watcher.Start();

        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.AwaitingStoryDirectory));

        Directory.CreateDirectory(Path.Combine(root, ".story", "tickets"));
        File.WriteAllText(Path.Combine(root, ".story", "config.json"), "{}");

        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));
    }

    [Fact]
    public void AMissingProjectRootIsReportedAsUnavailableRatherThanThrowing()
    {
        using var temp = new TempDirectory();

        using var watcher = Create(temp.Combine("never-existed"));
        watcher.Start();

        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Unavailable));
    }

    /// <summary>
    /// Deleting <c>.story</c> invalidates the underlying handle. The watcher has to notice and
    /// recover when the directory comes back — a branch switch does exactly this.
    /// </summary>
    [Fact]
    public void DeletingAndRecreatingTheStoryDirectoryRecoversTheWatch()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");

        using var watcher = Create(root);
        watcher.Start();
        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));

        Directory.Delete(Path.Combine(root, ".story"), recursive: true);
        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.AwaitingStoryDirectory));

        Directory.CreateDirectory(Path.Combine(root, ".story", "tickets"));
        File.WriteAllText(Path.Combine(root, ".story", "config.json"), "{}");

        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));

        var flushes = 0;
        watcher.Changed += (_, _) => Interlocked.Increment(ref flushes);
        TempDirectory.WriteTicket(root, "T-001");

        Assert.True(Wait.Until(() => Volatile.Read(ref flushes) > 0));
    }

    [Fact]
    public void RecoveringTheWatchForcesAFullReloadBecauseEventsWereMissed()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");
        var sections = new List<StorySection>();

        using var watcher = Create(root);
        watcher.Changed += (_, e) =>
        {
            lock (sections)
            {
                sections.Add(e.Sections);
            }
        };
        watcher.Start();
        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));

        Directory.Delete(Path.Combine(root, ".story"), recursive: true);
        Assert.True(Wait.Until(() => watcher.Status != WatcherStatus.Watching));

        Directory.CreateDirectory(Path.Combine(root, ".story"));
        File.WriteAllText(Path.Combine(root, ".story", "config.json"), "{}");

        Assert.True(Wait.Until(() =>
        {
            lock (sections)
            {
                return sections.Any(section => section == StorySection.All);
            }
        }));
    }

    [Fact]
    public void DisposingStopsDelivery()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");
        var flushes = 0;

        var watcher = Create(root);
        watcher.Changed += (_, _) => Interlocked.Increment(ref flushes);
        watcher.Start();
        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));

        watcher.Dispose();
        TempDirectory.WriteTicket(root, "T-001");
        Thread.Sleep(400);

        Assert.Equal(0, Volatile.Read(ref flushes));
    }

    [Fact]
    public void StartingTwiceIsHarmless()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");

        using var watcher = Create(root);
        watcher.Start();
        watcher.Start();

        Assert.True(Wait.Until(() => watcher.Status == WatcherStatus.Watching));
    }
}
