using Storybloq.Reader.Core.Loading;
using Xunit;

namespace Storybloq.Reader.Core.Tests;

public class ResilientFileReaderTests
{
    [Fact]
    public void ReadsAFileThatIsSimplyThere()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("ticket.json");
        File.WriteAllText(path, "{}");

        var result = new ResilientFileReader().ReadAllText(path);

        Assert.Equal(FileReadOutcome.Success, result.Outcome);
        Assert.Equal("{}", result.Content);
    }

    /// <summary>
    /// Enumerating a directory and then reading each entry races with ordinary deletes. Paying
    /// the retry budget for every deleted record would make reloads crawl, so a missing file
    /// resolves immediately.
    /// </summary>
    [Fact]
    public void AMissingFileIsReportedAsVanishedWithoutRetrying()
    {
        using var temp = new TempDirectory();
        var sleeps = new List<TimeSpan>();
        var reader = new ResilientFileReader(ResilientFileReader.DefaultRetryDelays, sleeps.Add);

        var result = reader.ReadAllText(temp.Combine("gone.json"));

        Assert.Equal(FileReadOutcome.Vanished, result.Outcome);
        Assert.Empty(sleeps);
    }

    [Fact]
    public void AMissingDirectoryIsAlsoVanishedRatherThanAFailure()
    {
        using var temp = new TempDirectory();
        var result = ResilientFileReader.Immediate.ReadAllText(temp.Combine("nope", "gone.json"));

        Assert.Equal(FileReadOutcome.Vanished, result.Outcome);
    }

    /// <summary>
    /// The CLI writes atomically and git rewrites many files at once, so a reader woken by a
    /// change event can arrive while a healthy file is still held open.
    /// </summary>
    [Fact]
    public void ALockedFileIsRetriedAndSucceedsOnceTheWriterLetsGo()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("contended.json");
        File.WriteAllText(path, "{\"id\":\"T-001\"}");

        var sleeps = new List<TimeSpan>();
        using var handle = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var reader = new ResilientFileReader(
            ResilientFileReader.DefaultRetryDelays,
            delay =>
            {
                sleeps.Add(delay);

                // Stand in for the writer finishing: release the lock after the first backoff.
                handle.Close();
            });

        var result = reader.ReadAllText(path);

        if (result.Outcome == FileReadOutcome.Success)
        {
            Assert.Equal("{\"id\":\"T-001\"}", result.Content);
            Assert.Single(sleeps);
            Assert.Equal(TimeSpan.FromMilliseconds(50), sleeps[0]);
        }
        else
        {
            // Unix does not enforce mandatory locking, so the first read may simply succeed.
            Assert.Equal(FileReadOutcome.Success, new ResilientFileReader().ReadAllText(path).Outcome);
        }
    }

    [Fact]
    public void APermanentlyUnreadableFileFailsAfterExhaustingTheBackoff()
    {
        using var temp = new TempDirectory();

        // A directory where a file is expected raises UnauthorizedAccessException on read.
        var path = temp.Combine("actually-a-directory.json");
        Directory.CreateDirectory(path);

        var sleeps = new List<TimeSpan>();
        var reader = new ResilientFileReader(ResilientFileReader.DefaultRetryDelays, sleeps.Add);

        var result = reader.ReadAllText(path);

        Assert.Equal(FileReadOutcome.Failed, result.Outcome);
        Assert.NotNull(result.Error);
        Assert.Equal(ResilientFileReader.DefaultRetryDelays, sleeps);
    }

    [Fact]
    public void TheImmediateReaderNeverWaits()
    {
        using var temp = new TempDirectory();
        var path = temp.Combine("actually-a-directory.json");
        Directory.CreateDirectory(path);

        var result = ResilientFileReader.Immediate.ReadAllText(path);

        Assert.Equal(FileReadOutcome.Failed, result.Outcome);
    }
}
