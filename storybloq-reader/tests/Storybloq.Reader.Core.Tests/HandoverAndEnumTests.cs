using Storybloq.Reader.Core.Models;
using Xunit;

namespace Storybloq.Reader.Core.Tests;

public class HandoverFileTests
{
    [Fact]
    public void ParsesTheCurrentTimestampedFormat()
    {
        var handover = HandoverFile.Parse("/x/2026-03-14-091500-a7f2c89b-phase-two-kickoff.md");

        Assert.Equal(new DateOnly(2026, 3, 14), handover.Date);
        Assert.Equal(new TimeOnly(9, 15, 0), handover.Time);
        Assert.Null(handover.Sequence);
        Assert.Equal("phase-two-kickoff", handover.Slug);
    }

    [Fact]
    public void ParsesTheLegacyDailySequenceFormat()
    {
        var handover = HandoverFile.Parse("/x/2026-02-01-02-second-session.md");

        Assert.Equal(new DateOnly(2026, 2, 1), handover.Date);
        Assert.Equal(2, handover.Sequence);
        Assert.Equal("second-session", handover.Slug);
    }

    [Fact]
    public void ParsesTheOldestDateOnlyFormat()
    {
        var handover = HandoverFile.Parse("/x/2026-01-15-initial-setup.md");

        Assert.Equal(new DateOnly(2026, 1, 15), handover.Date);
        Assert.Null(handover.Time);
        Assert.Null(handover.Sequence);
        Assert.Equal("initial-setup", handover.Slug);
    }

    /// <summary>A handover the user can see beats one silently dropped for having an odd name.</summary>
    [Fact]
    public void AnUnrecognisedNameIsStillSurfaced()
    {
        var handover = HandoverFile.Parse("/x/notes-from-yesterday.md");

        Assert.Null(handover.Date);
        Assert.Equal("notes-from-yesterday", handover.Slug);
        Assert.Equal("Notes From Yesterday", handover.Title);
    }

    /// <summary>
    /// The name still has the shape of a dated handover, so the slug is recovered; only the
    /// calendar-invalid date is dropped.
    /// </summary>
    [Fact]
    public void AnImpossibleDateYieldsNoDateButKeepsTheSlug()
    {
        var handover = HandoverFile.Parse("/x/2026-02-30-nonexistent-day.md");

        Assert.Null(handover.Date);
        Assert.Equal("nonexistent-day", handover.Slug);
    }

    [Fact]
    public void UndatedHandoversSortAfterDatedOnes()
    {
        var files = new List<HandoverFile>
        {
            HandoverFile.Parse("/x/loose-notes.md"),
            HandoverFile.Parse("/x/2026-01-15-initial-setup.md"),
            HandoverFile.Parse("/x/2026-03-14-091500-a7f2c89b-latest.md"),
        };

        files.Sort(HandoverFile.CompareNewestFirst);

        Assert.Equal(["latest", "initial-setup", "loose-notes"], files.Select(file => file.Slug));
    }

    [Fact]
    public void SameDayHandoversSortByTimeThenSequence()
    {
        var files = new List<HandoverFile>
        {
            HandoverFile.Parse("/x/2026-03-14-081500-11111111-morning.md"),
            HandoverFile.Parse("/x/2026-03-14-171500-22222222-evening.md"),
        };

        files.Sort(HandoverFile.CompareNewestFirst);

        Assert.Equal(["evening", "morning"], files.Select(file => file.Slug));
    }
}

public class StoryEnumTests
{
    [Theory]
    [InlineData("open", TicketStatus.Open)]
    [InlineData("inprogress", TicketStatus.InProgress)]
    [InlineData("complete", TicketStatus.Complete)]
    [InlineData("InProgress", TicketStatus.InProgress)]
    [InlineData("COMPLETE", TicketStatus.Complete)]
    public void WireValuesParseCaseInsensitively(string raw, TicketStatus expected)
    {
        var value = StoryEnum<TicketStatus>.FromRaw(raw);

        Assert.Equal(expected, value.Value);
        Assert.True(value.IsKnown);
        Assert.Equal(raw, value.Raw);
    }

    /// <summary>
    /// Enum.TryParse would happily accept "3" or "Open, Complete". Neither is a status, and
    /// accepting them would invent data the file never contained.
    /// </summary>
    [Theory]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("Open, Complete")]
    [InlineData("")]
    [InlineData("   ")]
    public void NumericAndCompositeInputsAreRejected(string raw)
    {
        var value = StoryEnum<TicketStatus>.FromRaw(raw);

        Assert.Equal(TicketStatus.Unknown, value.Value);
        Assert.False(value.IsKnown);
    }

    [Fact]
    public void AnUnknownValueKeepsItsRawTextForDisplay()
    {
        var value = StoryEnum<TicketStatus>.FromRaw("paused");

        Assert.False(value.IsKnown);
        Assert.True(value.IsUnrecognised);
        Assert.False(value.IsAbsent);
        Assert.Equal("paused", value.DisplayText);
    }

    [Fact]
    public void AbsenceIsNotTheSameAsAnUnknownValue()
    {
        var absent = StoryEnum<TicketStatus>.FromRaw(null);

        Assert.True(absent.IsAbsent);
        Assert.False(absent.IsUnrecognised);
        Assert.Equal("Unknown", absent.DisplayText);
    }

    [Fact]
    public void LifecycleTreatsAbsenceAndUnknownValuesAsLive()
    {
        Assert.True(StoryEnum<Lifecycle>.FromRaw(null).IsActive());
        Assert.True(StoryEnum<Lifecycle>.FromRaw("quarantined").IsActive());
        Assert.True(StoryEnum<Lifecycle>.FromRaw("active").IsActive());

        Assert.False(StoryEnum<Lifecycle>.FromRaw("deleted").IsActive());
        Assert.True(StoryEnum<Lifecycle>.FromRaw("deleted").IsDeleted());
        Assert.False(StoryEnum<Lifecycle>.FromRaw("archived").IsActive());
    }

    [Fact]
    public void SeverityOrdersWorstFirstAndPutsUnknownsLast()
    {
        var severities = new[] { "low", "critical", "unheard-of", "high", "medium" }
            .Select(StoryEnum<IssueSeverity>.FromRaw)
            .OrderBy(severity => severity.SortOrder())
            .Select(severity => severity.DisplayText);

        Assert.Equal(["critical", "high", "medium", "low", "unheard-of"], severities);
    }
}
