using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Storybloq.Reader.Core.Models;

/// <summary>
/// Fields every <c>.story/</c> record type carries. Mirrors the common tail of the upstream
/// zod schemas, including the <c>.passthrough()</c> behaviour via <see cref="Extra"/>.
/// </summary>
public abstract class StoryRecord
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// Present once a project has been through team-mode renumbering. Upstream prefers this
    /// over <see cref="Id"/> for anything user-facing.
    /// </summary>
    [JsonPropertyName("displayId")]
    public string? DisplayId { get; set; }

    [JsonPropertyName("previousDisplayIds")]
    public List<string>? PreviousDisplayIds { get; set; }

    [JsonPropertyName("lifecycle")]
    public StoryEnum<Lifecycle> Lifecycle { get; set; }

    [JsonPropertyName("rank")]
    public string? Rank { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("lastModifiedBy")]
    public string? LastModifiedBy { get; set; }

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public string? UpdatedAt { get; set; }

    [JsonPropertyName("deletedAt")]
    public string? DeletedAt { get; set; }

    [JsonPropertyName("deletedBy")]
    public string? DeletedBy { get; set; }

    [JsonPropertyName("_conflicts")]
    public List<JsonElement>? Conflicts { get; set; }

    /// <summary>Fields written by a newer Storybloq than this reader knows about.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>Absolute path of the file this record was read from. Not part of the format.</summary>
    [JsonIgnore]
    public string? SourcePath { get; set; }

    [JsonIgnore]
    public bool HasConflicts => Conflicts is { Count: > 0 };

    /// <summary>The identifier to show in the UI.</summary>
    [JsonIgnore]
    public string Label => DisplayId ?? Id ?? "(no id)";

    [JsonIgnore]
    public DateTimeOffset? UpdatedAtValue => StoryDate.ParseTimestamp(UpdatedAt);

    /// <summary>
    /// Every identifier this record has ever answered to. Blocker and relation references may
    /// still point at an id the record has since been renumbered away from.
    /// </summary>
    public IEnumerable<string> AllIdentifiers()
    {
        if (!string.IsNullOrEmpty(Id))
        {
            yield return Id;
        }

        if (!string.IsNullOrEmpty(DisplayId) && DisplayId != Id)
        {
            yield return DisplayId;
        }

        if (PreviousDisplayIds is null)
        {
            yield break;
        }

        foreach (var previous in PreviousDisplayIds)
        {
            if (!string.IsNullOrEmpty(previous))
            {
                yield return previous;
            }
        }
    }
}

/// <summary>Date parsing for the two shapes upstream writes: <c>YYYY-MM-DD</c> and loose ISO-8601.</summary>
public static class StoryDate
{
    public static DateOnly? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateOnly.TryParseExact(
            value,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out var parsed)
            ? parsed
            : null;
    }

    public static DateTimeOffset? ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed
            : null;
    }
}
