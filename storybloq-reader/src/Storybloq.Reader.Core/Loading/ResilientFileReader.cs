namespace Storybloq.Reader.Core.Loading;

public enum FileReadOutcome
{
    Success,

    /// <summary>The file disappeared between enumeration and read — an ordinary race, not an error.</summary>
    Vanished,

    /// <summary>Still unreadable after every retry.</summary>
    Failed,
}

public readonly record struct FileReadResult(FileReadOutcome Outcome, string? Content, Exception? Error)
{
    public static FileReadResult Success(string content) => new(FileReadOutcome.Success, content, null);

    public static FileReadResult Vanished() => new(FileReadOutcome.Vanished, null, null);

    public static FileReadResult Failed(Exception error) => new(FileReadOutcome.Failed, null, error);
}

/// <summary>
/// Reads text files with short retries.
/// </summary>
/// <remarks>
/// The Storybloq CLI writes atomically — temp file, then rename — and git operations rewrite
/// many <c>.story/</c> files at once. On Windows that means a reader woken by a change event can
/// arrive while the file is still held open, yielding a sharing violation on a file that is
/// perfectly healthy a few milliseconds later. Retrying briefly turns that into a non-event.
///
/// A missing file is treated differently: it is not retried, because enumerating a directory and
/// then reading each entry races with ordinary deletes, and paying the full retry budget for
/// every deleted ticket would make reloads crawl.
/// </remarks>
public sealed class ResilientFileReader
{
    public static IReadOnlyList<TimeSpan> DefaultRetryDelays { get; } =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(150),
        TimeSpan.FromMilliseconds(400),
    ];

    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly Action<TimeSpan> _sleep;

    public ResilientFileReader(IReadOnlyList<TimeSpan>? retryDelays = null, Action<TimeSpan>? sleep = null)
    {
        _retryDelays = retryDelays ?? DefaultRetryDelays;
        _sleep = sleep ?? Thread.Sleep;
    }

    /// <summary>A reader that never waits. For tests, and for the initial cold load.</summary>
    public static ResilientFileReader Immediate { get; } = new([], _ => { });

    public FileReadResult ReadAllText(string path)
    {
        Exception? lastError = null;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return FileReadResult.Success(File.ReadAllText(path));
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return FileReadResult.Vanished();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;

                if (attempt >= _retryDelays.Count)
                {
                    return FileReadResult.Failed(lastError);
                }

                _sleep(_retryDelays[attempt]);
            }
        }
    }
}
