using System.Globalization;
using System.Text.RegularExpressions;

namespace Storybloq.Reader.Core.Models;

/// <summary>
/// A file in <c>.story/handovers/</c>. Only the filename carries structure — upstream's
/// handover parser reads the name and returns the body verbatim, so this reader imposes no
/// section format on the markdown either.
/// </summary>
public sealed partial class HandoverFile
{
    private HandoverFile(string fileName, string path, DateOnly? date, TimeOnly? time, int? sequence, string slug)
    {
        FileName = fileName;
        Path = path;
        Date = date;
        Time = time;
        Sequence = sequence;
        Slug = slug;
    }

    public string FileName { get; }

    public string Path { get; }

    public DateOnly? Date { get; }

    /// <summary>Present only on the current filename format, which stamps a UTC time.</summary>
    public TimeOnly? Time { get; }

    /// <summary>Present only on the legacy daily-sequence format.</summary>
    public int? Sequence { get; }

    public string Slug { get; }

    /// <summary>The slug rendered back into prose for display.</summary>
    public string Title
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Slug))
            {
                return FileName;
            }

            var words = Slug.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', words.Select(word =>
                word.Length == 1 ? word.ToUpperInvariant() : char.ToUpperInvariant(word[0]) + word[1..]));
        }
    }

    // Current format: 2024-01-15-143022-a7f2c89b-my-slug.md (date, UTC time, entropy, slug).
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})-(\d{6})-([0-9a-fA-F]{8})-(.+)\.md$", RegexOptions.IgnoreCase)]
    private static partial Regex TimestampedPattern();

    // Legacy daily sequence: 2024-01-15-01-my-slug.md.
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})-(\d{2})-(.+)\.md$")]
    private static partial Regex SequencedPattern();

    // Oldest: 2024-01-15-my-slug.md.
    [GeneratedRegex(@"^(\d{4}-\d{2}-\d{2})-(.+)\.md$")]
    private static partial Regex DatedPattern();

    /// <summary>
    /// Parses a handover filename, trying the three formats upstream has written over time.
    /// An unrecognised name is still surfaced — with the whole name as its slug — because a
    /// handover the user can see is more useful than one silently dropped.
    /// </summary>
    public static HandoverFile Parse(string path)
    {
        var fileName = System.IO.Path.GetFileName(path);

        var timestamped = TimestampedPattern().Match(fileName);
        if (timestamped.Success)
        {
            return new HandoverFile(
                fileName,
                path,
                ParseDate(timestamped.Groups[1].Value),
                ParseTime(timestamped.Groups[2].Value),
                sequence: null,
                timestamped.Groups[4].Value);
        }

        var sequenced = SequencedPattern().Match(fileName);
        if (sequenced.Success)
        {
            return new HandoverFile(
                fileName,
                path,
                ParseDate(sequenced.Groups[1].Value),
                time: null,
                int.Parse(sequenced.Groups[2].Value, CultureInfo.InvariantCulture),
                sequenced.Groups[3].Value);
        }

        var dated = DatedPattern().Match(fileName);
        if (dated.Success)
        {
            return new HandoverFile(
                fileName,
                path,
                ParseDate(dated.Groups[1].Value),
                time: null,
                sequence: null,
                dated.Groups[2].Value);
        }

        var withoutExtension = fileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? fileName[..^3]
            : fileName;
        return new HandoverFile(fileName, path, date: null, time: null, sequence: null, withoutExtension);
    }

    private static DateOnly? ParseDate(string value) => StoryDate.Parse(value);

    private static TimeOnly? ParseTime(string value) =>
        TimeOnly.TryParseExact(value, "HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed
            : null;

    /// <summary>Newest first, with undated files last but still visible.</summary>
    public static int CompareNewestFirst(HandoverFile left, HandoverFile right)
    {
        var byDate = Nullable.Compare(right.Date, left.Date);
        if (byDate != 0)
        {
            // Undated sorts last regardless of direction.
            if (left.Date is null)
            {
                return 1;
            }

            if (right.Date is null)
            {
                return -1;
            }

            return byDate;
        }

        var byTime = Nullable.Compare(right.Time, left.Time);
        if (byTime != 0)
        {
            return byTime;
        }

        var bySequence = Nullable.Compare(right.Sequence, left.Sequence);
        return bySequence != 0
            ? bySequence
            : string.Compare(right.FileName, left.FileName, StringComparison.OrdinalIgnoreCase);
    }
}
