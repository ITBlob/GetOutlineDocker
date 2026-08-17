using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.State;
using Storybloq.Reader.Core.Watching;
using Storybloq.Reader.Platform.Projects;
using Xunit;

namespace Storybloq.Reader.Platform.Tests;

public class ProjectSessionTests
{
    private static ProjectSession Create(string root, out FakeStoryWatcher watcher)
    {
        var fake = new FakeStoryWatcher(root);
        watcher = fake;
        return new ProjectSession(root, new StoryProjectLoader(ResilientFileReader.Immediate), _ => fake);
    }

    [Fact]
    public void StartLoadsTheProjectAndBeginsWatching()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");
        TempDirectory.WriteTicket(root, "T-001");

        using var session = Create(root, out var watcher);
        session.Start();

        Assert.True(watcher.Started);
        Assert.Equal("aurora", session.Project.DisplayName);
        Assert.Single(session.State.LeafTickets);
    }

    [Fact]
    public void AChangeEventReloadsAndPublishesNewState()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");
        TempDirectory.WriteTicket(root, "T-001");

        using var session = Create(root, out var watcher);
        session.Start();

        var updates = new List<ProjectSessionUpdatedEventArgs>();
        session.Updated += (_, e) => updates.Add(e);

        TempDirectory.WriteTicket(root, "T-002");
        watcher.RaiseChanged(StorySection.Tickets);

        var update = Assert.Single(updates);
        Assert.Equal(StorySection.Tickets, update.Sections);
        Assert.Equal(2, session.State.LeafTickets.Count);
    }

    [Fact]
    public void EachUpdatePublishesAWholeNewStateRatherThanMutatingTheOldOne()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");
        TempDirectory.WriteTicket(root, "T-001");

        using var session = Create(root, out var watcher);
        session.Start();
        var before = session.State;

        TempDirectory.WriteTicket(root, "T-002");
        watcher.RaiseChanged(StorySection.Tickets);

        Assert.NotSame(before, session.State);
        Assert.Single(before.LeafTickets); // the old snapshot is untouched
    }

    [Fact]
    public void AnEmptyChangeSetDoesNotReload()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");

        using var session = Create(root, out var watcher);
        session.Start();

        var updates = 0;
        session.Updated += (_, _) => updates++;
        watcher.RaiseChanged(StorySection.None);

        Assert.Equal(0, updates);
    }

    /// <summary>
    /// The first successful arm must not trigger a second full load — Start has already read
    /// the project, and doubling cold-start cost across a dozen projects is noticeable.
    /// </summary>
    [Fact]
    public void TheInitialWatchDoesNotCauseARedundantSecondLoad()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");

        using var session = Create(root, out _);

        var updates = 0;
        session.Updated += (_, _) => updates++;
        session.Start();

        Assert.Equal(1, updates);
    }

    [Fact]
    public void RecoveringFromAnUnavailableWatchReloadsEverything()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");

        using var session = Create(root, out var watcher);
        session.Start();

        var sections = new List<StorySection>();
        session.Updated += (_, e) => sections.Add(e.Sections);

        watcher.RaiseStatus(WatcherStatus.Unavailable);
        watcher.RaiseStatus(WatcherStatus.Watching);

        Assert.Equal([StorySection.All], sections);
    }

    [Fact]
    public void DisposeStopsTheWatcherAndIgnoresLateEvents()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");

        var session = Create(root, out var watcher);
        session.Start();
        session.Dispose();

        Assert.True(watcher.Disposed);

        var updates = 0;
        session.Updated += (_, _) => updates++;
        watcher.RaiseChanged(StorySection.Tickets);

        Assert.Equal(0, updates);
    }
}

public class ProjectWorkspaceTests
{
    private static ProjectWorkspace Create(TempDirectory temp, int maxWatched = 12) =>
        new(
            new ProjectRegistryStore(temp.Combine("projects.json")),
            root => new ProjectSession(
                root,
                new StoryProjectLoader(ResilientFileReader.Immediate),
                watchedRoot => new FakeStoryWatcher(watchedRoot)),
            maxWatched);

    [Fact]
    public void AddingADirectoryInsideAProjectResolvesToItsRoot()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");
        var nested = Path.Combine(root, "src", "components");
        Directory.CreateDirectory(nested);

        using var workspace = Create(temp);
        var result = workspace.Add(nested);

        Assert.Equal(AddProjectOutcome.Added, result.Outcome);
        Assert.Equal(Path.GetFullPath(root), result.Session!.Root);
    }

    [Fact]
    public void AddingSomethingThatIsNotAProjectExplainsWhy()
    {
        using var temp = new TempDirectory();
        var directory = temp.Combine("just-a-folder");
        Directory.CreateDirectory(directory);

        using var workspace = Create(temp);
        var result = workspace.Add(directory);

        Assert.Equal(AddProjectOutcome.NotAStorybloqProject, result.Outcome);
        Assert.Contains("config.json", result.Message);
        Assert.Empty(workspace.Sessions);
    }

    [Fact]
    public void AddingTheSameProjectTwiceIsANoOp()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");

        using var workspace = Create(temp);
        workspace.Add(root);
        var second = workspace.Add(Path.Combine(root, "..", "aurora"));

        Assert.Equal(AddProjectOutcome.AlreadyPresent, second.Outcome);
        Assert.Single(workspace.Sessions);
    }

    [Fact]
    public void ProjectsSurviveARestart()
    {
        using var temp = new TempDirectory();
        var first = temp.CreateProject("aurora");
        var second = temp.CreateProject("beacon");

        using (var workspace = Create(temp))
        {
            workspace.Add(first);
            workspace.Add(second);
        }

        using var reopened = Create(temp);
        reopened.LoadSaved();

        Assert.Equal(
            [Path.GetFullPath(first), Path.GetFullPath(second)],
            reopened.Sessions.Select(session => session.Root));
    }

    [Fact]
    public void RemovingAProjectDropsItFromTheSavedList()
    {
        using var temp = new TempDirectory();
        var root = temp.CreateProject("aurora");

        using var workspace = Create(temp);
        workspace.Add(root);

        Assert.True(workspace.Remove(root));
        Assert.Empty(workspace.Sessions);
        Assert.False(workspace.Remove(root));

        using var reopened = Create(temp);
        reopened.LoadSaved();
        Assert.Empty(reopened.Sessions);
    }

    [Fact]
    public void AttentionRollsUpAcrossEveryProject()
    {
        using var temp = new TempDirectory();
        var first = temp.CreateProject("aurora");
        var second = temp.CreateProject("beacon");

        WriteCriticalIssue(first, "ISS-001", "Aurora is on fire");
        WriteCriticalIssue(second, "ISS-001", "Beacon is on fire");

        using var workspace = Create(temp);
        workspace.Add(first);
        workspace.Add(second);

        var attention = workspace.Attention;

        Assert.Equal(2, attention.Count);
        Assert.Equal(["aurora", "beacon"], attention.Select(item => item.ProjectName));
        Assert.All(attention, item => Assert.Equal(AttentionKind.Issue, item.Kind));
    }

    /// <summary>
    /// Someone tracking a dozen repositories should not pay for a dozen live watchers; the
    /// least recently used one is retired rather than refused.
    /// </summary>
    [Fact]
    public void OnlyTheMostRecentlyUsedProjectsStayWatched()
    {
        using var temp = new TempDirectory();
        var roots = Enumerable.Range(1, 3).Select(i => temp.CreateProject($"p{i}")).ToList();

        using var workspace = Create(temp, maxWatched: 2);
        foreach (var root in roots)
        {
            workspace.Add(root);
        }

        var sessions = workspace.Sessions;

        // p1 was pushed out by p2 and p3.
        Assert.False(sessions[0].IsWatching);
        Assert.True(sessions[1].IsWatching);
        Assert.True(sessions[2].IsWatching);

        // Selecting p1 again brings it back and retires the oldest of the others.
        workspace.Touch(roots[0]);

        Assert.True(sessions[0].IsWatching);
        Assert.False(sessions[1].IsWatching);
    }

    [Fact]
    public void TouchingAnUnknownProjectIsIgnored()
    {
        using var temp = new TempDirectory();
        using var workspace = Create(temp);

        workspace.Touch(temp.Combine("nope"));

        Assert.Empty(workspace.Sessions);
    }

    private static void WriteCriticalIssue(string projectRoot, string id, string title)
    {
        var directory = Path.Combine(projectRoot, ".story", "issues");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, $"{id}.json"),
            $$"""
            { "id": "{{id}}", "title": "{{title}}", "status": "open", "severity": "critical",
              "components": [], "impact": "", "location": "", "discoveredDate": "2026-01-01",
              "relatedTickets": [], "updatedAt": "2026-01-01T00:00:00Z" }
            """);
    }
}

public class ProjectRegistryStoreTests
{
    [Fact]
    public void AMissingFileLoadsAsAnEmptyList()
    {
        using var temp = new TempDirectory();
        Assert.Empty(new ProjectRegistryStore(temp.Combine("nothing.json")).Load());
    }

    [Fact]
    public void SavedProjectsRoundTrip()
    {
        using var temp = new TempDirectory();
        var store = new ProjectRegistryStore(temp.Combine("projects.json"));

        store.Save([new RegisteredProject { Root = @"C:\repos\aurora", Nickname = "Aurora" }]);
        var loaded = Assert.Single(store.Load());

        Assert.Equal(@"C:\repos\aurora", loaded.Root);
        Assert.Equal("Aurora", loaded.Nickname);
    }

    [Fact]
    public void SavingCreatesTheSettingsDirectory()
    {
        using var temp = new TempDirectory();
        var store = new ProjectRegistryStore(temp.Combine("nested", "dir", "projects.json"));

        store.Save([new RegisteredProject { Root = "/repos/aurora" }]);

        Assert.Single(store.Load());
    }

    /// <summary>A damaged settings file must not stop the app from starting.</summary>
    [Fact]
    public void ACorruptFileLoadsAsEmptyRatherThanThrowing()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("projects.json");
        File.WriteAllText(path, "{ not json at all");

        Assert.Empty(new ProjectRegistryStore(path).Load());
    }

    [Fact]
    public void EntriesWithoutARootAreDiscarded()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("projects.json");
        File.WriteAllText(path, """[ { "root": "" }, { "root": "/repos/aurora" } ]""");

        var loaded = Assert.Single(new ProjectRegistryStore(path).Load());
        Assert.Equal("/repos/aurora", loaded.Root);
    }

    [Fact]
    public void SavingTwiceReplacesRatherThanAppends()
    {
        using var temp = new TempDirectory();
        var store = new ProjectRegistryStore(temp.Combine("projects.json"));

        store.Save([new RegisteredProject { Root = "/a" }]);
        store.Save([new RegisteredProject { Root = "/b" }]);

        var loaded = Assert.Single(store.Load());
        Assert.Equal("/b", loaded.Root);
    }
}
