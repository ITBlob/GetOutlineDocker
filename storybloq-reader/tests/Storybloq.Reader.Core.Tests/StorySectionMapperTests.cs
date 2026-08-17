using Storybloq.Reader.Core.Loading;
using Xunit;

namespace Storybloq.Reader.Core.Tests;

public class StorySectionMapperTests
{
    [Theory]
    [InlineData("config.json", StorySection.Config)]
    [InlineData("roadmap.json", StorySection.Roadmap)]
    [InlineData("tickets/T-001.json", StorySection.Tickets)]
    [InlineData("issues/ISS-001.json", StorySection.Issues)]
    [InlineData("notes/N-001.json", StorySection.Notes)]
    [InlineData("lessons/L-001.json", StorySection.Lessons)]
    [InlineData("handovers/2026-01-15-initial-setup.md", StorySection.Handovers)]
    [InlineData("tickets", StorySection.Tickets)]
    public void MapsPathsToTheirSection(string path, StorySection expected) =>
        Assert.Equal(expected, StorySectionMapper.Map(path));

    /// <summary>
    /// FileSystemWatcher hands back backslash-separated paths in whatever case the caller used,
    /// so Core has to classify those identically to the forward-slash form the tests use.
    /// </summary>
    [Theory]
    [InlineData(@"tickets\T-001.json", StorySection.Tickets)]
    [InlineData(@"Tickets\T-001.json", StorySection.Tickets)]
    [InlineData(@"HANDOVERS\x.md", StorySection.Handovers)]
    [InlineData("Config.JSON", StorySection.Config)]
    public void PathHandlingIsSeparatorAgnosticAndCaseInsensitive(string path, StorySection expected) =>
        Assert.Equal(expected, StorySectionMapper.Map(path));

    /// <summary>
    /// snapshots/ and bus/ are gitignored runtime state. Upstream rewrites them constantly, and
    /// reloading the project on that churn would keep the sidebar permanently busy for no gain.
    /// </summary>
    [Theory]
    [InlineData("snapshots/2026-03-14T09-00-00.json")]
    [InlineData(@"snapshots\latest.json")]
    [InlineData("bus/mailbox.json")]
    [InlineData("bus/threads/t1.json")]
    public void GitignoredRuntimeStateIsIgnored(string path) =>
        Assert.Equal(StorySection.None, StorySectionMapper.Map(path));

    /// <summary>Atomic writes land as temp files first; those must never trigger a reload.</summary>
    [Theory]
    [InlineData("tickets/T-001.json.tmp")]
    [InlineData("tickets/T-001.json.swp")]
    [InlineData("tickets/T-001.json~")]
    [InlineData("tickets/.T-001.json")]
    [InlineData("tickets/T-001.json.lock")]
    public void TransientWriterArtefactsAreIgnored(string path) =>
        Assert.Equal(StorySection.None, StorySectionMapper.Map(path));

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(".gitignore")]
    [InlineData("unknown-directory/file.json")]
    [InlineData("readme.md")]
    public void UnrecognisedPathsMapToNothing(string? path) =>
        Assert.Equal(StorySection.None, StorySectionMapper.Map(path));

    [Fact]
    public void NestedPathsBelowASectionStillMapToThatSection() =>
        Assert.Equal(StorySection.Tickets, StorySectionMapper.Map("tickets/archive/2025/T-001.json"));
}
