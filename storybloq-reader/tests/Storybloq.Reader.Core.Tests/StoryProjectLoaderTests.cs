using Storybloq.Reader.Core.Loading;
using Storybloq.Reader.Core.Models;
using Xunit;

namespace Storybloq.Reader.Core.Tests;

public class StoryProjectLoaderTests
{
    [Fact]
    public void LoadsEveryRecordTypeFromASampleProject()
    {
        var project = Fixtures.Load(Fixtures.SampleProject);

        Assert.Equal("Aurora", project.DisplayName);
        Assert.Equal("typescript", project.Config?.Language);
        Assert.Equal(11, project.Tickets.Count);
        Assert.Equal(5, project.Issues.Count);
        Assert.Equal(2, project.Notes.Count);
        Assert.Equal(2, project.Lessons.Count);
        Assert.Equal(3, project.Handovers.Count);
        Assert.Equal(3, project.Roadmap?.PhaseList.Count);
        Assert.Empty(project.Diagnostics);
    }

    [Fact]
    public void RecordsCarryTheirSourcePath()
    {
        var project = Fixtures.Load(Fixtures.SampleProject);
        var ticket = project.Tickets.Single(t => t.Id == "T-001");

        Assert.NotNull(ticket.SourcePath);
        Assert.EndsWith("T-001.json", ticket.SourcePath);
    }

    [Fact]
    public void UnknownFieldsSurviveAsExtensionData()
    {
        var project = Fixtures.Load(Fixtures.SampleProject);

        // Passthrough at the config level.
        Assert.True(project.Config!.Extra!.ContainsKey("futureSetting"));

        // ...and on individual records.
        var ticket = project.Tickets.Single(t => t.Id == "T-010");
        Assert.True(ticket.Extra!.ContainsKey("reviewLens"));
    }

    [Fact]
    public void UnknownEnumValueLoadsAndKeepsItsRawText()
    {
        var project = Fixtures.Load(Fixtures.SampleProject);
        var ticket = project.Tickets.Single(t => t.Id == "T-010");

        Assert.False(ticket.Status.IsKnown);
        Assert.True(ticket.Status.IsUnrecognised);
        Assert.Equal("paused", ticket.Status.Raw);
        Assert.Equal("paused", ticket.Status.DisplayText);
        Assert.Equal(TicketStatus.Unknown, ticket.Status.Value);
    }

    [Fact]
    public void AbsentEnumIsDistinguishableFromUnrecognisedEnum()
    {
        var project = Fixtures.Load(Fixtures.SampleProject);
        var ticket = project.Tickets.Single(t => t.Id == "T-001");

        // No lifecycle written at all.
        Assert.True(ticket.Lifecycle.IsAbsent);
        Assert.False(ticket.Lifecycle.IsUnrecognised);
        Assert.True(ticket.Lifecycle.IsActive());
    }

    [Fact]
    public void CanonicalIdentifiersAndDisplayIdsBothLoad()
    {
        var project = Fixtures.Load(Fixtures.SampleProject);
        var ticket = project.Tickets.Single(t => t.Id == "t-0abcdefghjkmnpqr");

        Assert.Equal("T-009", ticket.DisplayId);
        Assert.Equal("T-009", ticket.Label);
        Assert.Equal(["T-104"], ticket.PreviousDisplayIds);
        Assert.Equal("Ökonomie der Ränder — 日本語 tickets", ticket.Title);
    }

    [Fact]
    public void IssueSourceRefsLoadWithTheirProvenance()
    {
        var project = Fixtures.Load(Fixtures.SampleProject);
        var issue = project.Issues.Single(i => i.Id == "ISS-004");
        var reference = Assert.Single(issue.SourceRefs!);

        Assert.Equal("app/sync/retry.ts", reference.Path);
        Assert.Equal(42, reference.StartLine);
        Assert.Equal("app/sync/retry.ts:42-68", reference.DisplayLocation);
        Assert.Equal("9f2c1ab4", reference.Revision);
    }

    [Fact]
    public void MissingRecordDirectoriesAreNormalRatherThanErrors()
    {
        var project = Fixtures.Load(Fixtures.MinimalProject);

        Assert.Equal("Minimal", project.DisplayName);
        Assert.Empty(project.Tickets);
        Assert.Empty(project.Issues);
        Assert.Empty(project.Handovers);
        Assert.Null(project.Roadmap);
        Assert.Empty(project.Diagnostics);
    }

    [Fact]
    public void FeatureFlagsAreReadFromConfig()
    {
        var minimal = Fixtures.Load(Fixtures.MinimalProject);

        Assert.True(minimal.Features.TicketsEnabled);
        Assert.False(minimal.Features.HandoversEnabled);
        Assert.False(minimal.Features.RoadmapEnabled);
    }

    [Fact]
    public void CorruptFilesAreIsolatedToDiagnosticsAndHealthyRecordsStillLoad()
    {
        var project = Fixtures.Load(Fixtures.BrokenProject);

        // The one healthy ticket survives its three broken neighbours.
        var ticket = Assert.Single(project.Tickets);
        Assert.Equal("T-001", ticket.Id);

        Assert.Contains(project.Diagnostics, d => d.FileName == "T-002.json" && d.Severity == DiagnosticSeverity.Error);
        Assert.Contains(project.Diagnostics, d => d.FileName == "T-003.json" && d.Message.Contains("empty", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(project.Diagnostics, d => d.FileName == "T-004.json" && d.Message.Contains("no id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(project.Diagnostics, d => d.FileName == "roadmap.json" && d.Severity == DiagnosticSeverity.Error);

        // Config parsed fine, so the project still has an identity to show.
        Assert.Equal("Broken", project.DisplayName);
        Assert.True(project.HasErrors);
    }

    [Fact]
    public void FractionalNumberWhereAnIntegerWasExpectedDoesNotLoseTheRecord()
    {
        var project = Fixtures.Load(Fixtures.BrokenProject);
        var issue = Assert.Single(project.Issues);

        Assert.Equal("ISS-001", issue.Id);
        Assert.Equal(2, issue.Order);
    }

    [Fact]
    public void HandoversAreSortedNewestFirstAcrossAllFilenameFormats()
    {
        var project = Fixtures.Load(Fixtures.SampleProject);

        Assert.Collection(
            project.Handovers,
            handover =>
            {
                Assert.Equal(new DateOnly(2026, 3, 14), handover.Date);
                Assert.Equal(new TimeOnly(9, 15, 0), handover.Time);
                Assert.Equal("phase-two-kickoff", handover.Slug);
                Assert.Equal("Phase Two Kickoff", handover.Title);
            },
            handover =>
            {
                Assert.Equal(new DateOnly(2026, 2, 1), handover.Date);
                Assert.Equal(2, handover.Sequence);
                Assert.Equal("second-session", handover.Slug);
            },
            handover =>
            {
                Assert.Equal(new DateOnly(2026, 1, 15), handover.Date);
                Assert.Null(handover.Time);
                Assert.Null(handover.Sequence);
                Assert.Equal("initial-setup", handover.Slug);
            });
    }

    [Fact]
    public void HandoverBodiesAreReadOnDemand()
    {
        var loader = new StoryProjectLoader(ResilientFileReader.Immediate);
        var project = loader.Load(Fixtures.SampleProject);
        var latest = project.Handovers[0];

        var result = loader.ReadHandoverContent(latest);

        Assert.Equal(FileReadOutcome.Success, result.Outcome);
        Assert.Contains("Phase two kickoff", result.Content);
    }

    [Fact]
    public void MissingStoryDirectoryIsReportedRatherThanThrown()
    {
        using var temp = new TempDirectory();
        var project = Fixtures.Load(temp.Path);

        var diagnostic = Assert.Single(project.Diagnostics);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Contains("No .story directory", diagnostic.Message);
    }

    [Fact]
    public void PartialReloadReplacesOnlyTheRequestedSection()
    {
        using var temp = new TempDirectory();
        var root = CopyFixture(Fixtures.SampleProject, temp.Combine("aurora"));
        var loader = new StoryProjectLoader(ResilientFileReader.Immediate);

        var first = loader.Load(root);

        // Change a ticket and an issue, then reload tickets only.
        File.WriteAllText(
            Path.Combine(root, ".story", "tickets", "T-004.json"),
            """{ "id": "T-004", "title": "Renamed", "type": "feature", "status": "complete", "phase": "p2", "order": 4, "createdDate": "2026-01-25", "blockedBy": [], "updatedAt": "2026-03-20T10:00:00Z" }""");
        File.Delete(Path.Combine(root, ".story", "issues", "ISS-003.json"));

        var second = loader.Reload(first, StorySection.Tickets);

        Assert.Equal("Renamed", second.Tickets.Single(t => t.Id == "T-004").Title);

        // Issues were not re-read, so the deleted file is still represented.
        Assert.Equal(5, second.Issues.Count);
        Assert.Same(first.Issues, second.Issues);
        Assert.Same(first.Handovers, second.Handovers);
    }

    [Fact]
    public void PartialReloadClearsOnlyItsOwnDiagnostics()
    {
        using var temp = new TempDirectory();
        var root = CopyFixture(Fixtures.BrokenProject, temp.Combine("broken"));
        var loader = new StoryProjectLoader(ResilientFileReader.Immediate);

        var first = loader.Load(root);
        Assert.Contains(first.Diagnostics, d => d.Section == StorySection.Roadmap);
        Assert.Contains(first.Diagnostics, d => d.Section == StorySection.Tickets);

        // Repair the tickets, reload only tickets.
        foreach (var file in new[] { "T-002.json", "T-003.json", "T-004.json" })
        {
            File.Delete(Path.Combine(root, ".story", "tickets", file));
        }

        var second = loader.Reload(first, StorySection.Tickets);

        Assert.DoesNotContain(second.Diagnostics, d => d.Section == StorySection.Tickets);

        // The roadmap is still broken and must still be reported.
        Assert.Contains(second.Diagnostics, d => d.Section == StorySection.Roadmap);
    }

    private static string CopyFixture(string source, string destination)
    {
        foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        }

        Directory.CreateDirectory(destination);
        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination, StringComparison.Ordinal);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }

        return destination;
    }
}
