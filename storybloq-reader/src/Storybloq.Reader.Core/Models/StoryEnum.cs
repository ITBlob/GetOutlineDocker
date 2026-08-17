using System.Diagnostics.CodeAnalysis;

namespace Storybloq.Reader.Core.Models;

/// <summary>
/// A tolerantly-parsed enum value that never loses the original wire text.
/// </summary>
/// <remarks>
/// Every upstream zod schema is <c>.passthrough()</c>, and their own comment on
/// <c>FeaturesSchema</c> says this exists "to allow future fields without breaking older
/// readers". Enum members deserve the same courtesy: if a future Storybloq release adds a
/// ticket status this reader has never heard of, the file must still load and the UI must
/// still be able to show the raw string rather than silently mislabelling the record.
/// </remarks>
public readonly record struct StoryEnum<TEnum>
    where TEnum : struct, Enum
{
    public StoryEnum(TEnum value, string? raw)
    {
        Value = value;
        Raw = raw;
    }

    /// <summary>Parsed value, or the enum's zero member when absent or unrecognised.</summary>
    public TEnum Value { get; }

    /// <summary>The literal JSON text, retained even when <see cref="Value"/> is the zero member.</summary>
    public string? Raw { get; }

    /// <summary>True when the wire value mapped onto a member this reader understands.</summary>
    public bool IsKnown => !EqualityComparer<TEnum>.Default.Equals(Value, default);

    /// <summary>True when the field was absent or JSON null.</summary>
    public bool IsAbsent => Raw is null;

    /// <summary>
    /// True when a value was present but this reader does not recognise it — the case worth
    /// surfacing in the UI, as distinct from a field that was simply never written.
    /// </summary>
    public bool IsUnrecognised => Raw is not null && !IsKnown;

    public static StoryEnum<TEnum> Absent => default;

    public static StoryEnum<TEnum> FromRaw(string? raw)
    {
        if (raw is null)
        {
            return Absent;
        }

        return new StoryEnum<TEnum>(Parse(raw), raw);
    }

    /// <summary>
    /// Wire values are lowercase with no separators ("inprogress"), so a case-insensitive
    /// member-name match covers every value upstream defines. Numeric and flag-list inputs
    /// that <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/> would otherwise accept
    /// are rejected — "3" is not a status.
    /// </summary>
    private static TEnum Parse(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.Length == 0 || !trimmed.All(char.IsLetter))
        {
            return default;
        }

        if (Enum.TryParse<TEnum>(trimmed, ignoreCase: true, out var parsed)
            && Enum.IsDefined(typeof(TEnum), parsed))
        {
            return parsed;
        }

        return default;
    }

    public bool Is(TEnum candidate) => EqualityComparer<TEnum>.Default.Equals(Value, candidate);

    /// <summary>Text for display: the raw wire value when present, else the member name.</summary>
    public string DisplayText => Raw ?? Value.ToString();

    public override string ToString() => DisplayText;

    public static implicit operator TEnum(StoryEnum<TEnum> value) => value.Value;
}

public static class StoryEnumExtensions
{
    /// <summary>
    /// Lifecycle is optional on every record type, and its absence means the record is live.
    /// Only an explicit "deleted" hides a record.
    /// </summary>
    public static bool IsDeleted(this StoryEnum<Lifecycle> lifecycle) => lifecycle.Is(Lifecycle.Deleted);

    public static bool IsArchived(this StoryEnum<Lifecycle> lifecycle) => lifecycle.Is(Lifecycle.Archived);

    /// <summary>
    /// "Active" in upstream's aggregation pipeline: neither archived nor deleted. An absent or
    /// unrecognised lifecycle counts as active so unknown future values never silently drop
    /// records out of the counts.
    /// </summary>
    public static bool IsActive(this StoryEnum<Lifecycle> lifecycle) =>
        !lifecycle.IsDeleted() && !lifecycle.IsArchived();

    [SuppressMessage("Style", "IDE0072", Justification = "Exhaustive over known members.")]
    public static int SortOrder(this StoryEnum<IssueSeverity> severity) => severity.Value switch
    {
        IssueSeverity.Critical => 0,
        IssueSeverity.High => 1,
        IssueSeverity.Medium => 2,
        IssueSeverity.Low => 3,
        _ => 4,
    };
}
