using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.Watching;

namespace Storybloq.Reader.Platform.Tests;

internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "storybloq-reader-platform-tests",
            Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    /// <summary>Creates a project root with a valid <c>.story/config.json</c> and a tickets directory.</summary>
    public string CreateProject(string name)
    {
        var root = Combine(name);
        var story = System.IO.Path.Combine(root, ".story");
        Directory.CreateDirectory(System.IO.Path.Combine(story, "tickets"));
        File.WriteAllText(
            System.IO.Path.Combine(story, "config.json"),
            $$"""{ "version": "0.1.5", "project": "{{name}}", "type": "app", "language": "ts", "features": {} }""");
        return root;
    }

    public static void WriteTicket(string projectRoot, string id, string status = "open")
    {
        var path = System.IO.Path.Combine(projectRoot, ".story", "tickets", $"{id}.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            $$"""
            { "id": "{{id}}", "title": "{{id}}", "description": "", "type": "task", "status": "{{status}}",
              "phase": null, "order": 1, "createdDate": "2026-01-01", "completedDate": null,
              "blockedBy": [], "updatedAt": "2026-01-01T00:00:00Z" }
            """);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }
}

/// <summary>A watcher whose events the test raises by hand, for deterministic session tests.</summary>
internal sealed class FakeStoryWatcher(string projectRoot) : IStoryWatcher
{
    public string ProjectRoot { get; } = projectRoot;

    public WatcherStatus Status { get; private set; } = WatcherStatus.Unavailable;

    public bool Started { get; private set; }

    public bool Disposed { get; private set; }

    public event EventHandler<StoryChangedEventArgs>? Changed;

    public event EventHandler<WatcherStatusEventArgs>? StatusChanged;

    public void Start()
    {
        Started = true;
        RaiseStatus(WatcherStatus.Watching);
    }

    public void RaiseChanged(StorySection sections) => Changed?.Invoke(this, new StoryChangedEventArgs(sections));

    public void RaiseStatus(WatcherStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, new WatcherStatusEventArgs(status));
    }

    public void Dispose() => Disposed = true;
}

internal static class Wait
{
    public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Polls until the condition holds, so tests never depend on a fixed sleep.</summary>
    public static bool Until(Func<bool> condition, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? Timeout);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(20);
        }

        return condition();
    }
}
