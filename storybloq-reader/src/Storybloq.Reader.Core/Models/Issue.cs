using System.Text.Json.Serialization;

namespace Storybloq.Reader.Core.Models;

/// <summary>A record from <c>.story/issues/</c>.</summary>
public sealed class Issue : StoryRecord
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("status")]
    public StoryEnum<IssueStatus> Status { get; set; }

    [JsonPropertyName("severity")]
    public StoryEnum<IssueSeverity> Severity { get; set; }

    [JsonPropertyName("components")]
    public List<string>? Components { get; set; }

    [JsonPropertyName("impact")]
    public string? Impact { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("order")]
    public int? Order { get; set; }

    [JsonPropertyName("discoveredDate")]
    public string? DiscoveredDate { get; set; }

    [JsonPropertyName("resolvedDate")]
    public string? ResolvedDate { get; set; }

    [JsonPropertyName("relatedTickets")]
    public List<string>? RelatedTickets { get; set; }

    [JsonPropertyName("assignedTo")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("dedupeKey")]
    public string? DedupeKey { get; set; }

    /// <summary>
    /// Durable review evidence. Upstream deliberately stores provenance — commit, content hash,
    /// review id — and never source excerpts, so there is no code text to render here.
    /// </summary>
    [JsonPropertyName("sourceRefs")]
    public List<IssueSourceRef>? SourceRefs { get; set; }

    [JsonIgnore]
    public bool IsResolved => Status.Is(IssueStatus.Resolved);

    /// <summary>Unresolved and severe enough to demand attention across projects.</summary>
    [JsonIgnore]
    public bool NeedsAttention =>
        !IsResolved && (Severity.Is(IssueSeverity.Critical) || Severity.Is(IssueSeverity.High));

    [JsonIgnore]
    public DateOnly? DiscoveredDateValue => StoryDate.Parse(DiscoveredDate);
}

public sealed class IssueSourceRef
{
    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("startLine")]
    public int? StartLine { get; set; }

    [JsonPropertyName("endLine")]
    public int? EndLine { get; set; }

    /// <summary>Git object id the reference was captured against.</summary>
    [JsonPropertyName("revision")]
    public string? Revision { get; set; }

    [JsonPropertyName("snapshotId")]
    public string? SnapshotId { get; set; }

    [JsonPropertyName("contentHash")]
    public string? ContentHash { get; set; }

    [JsonPropertyName("reviewId")]
    public string? ReviewId { get; set; }

    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public string DisplayLocation => (Path, StartLine, EndLine) switch
    {
        (null or "", _, _) => "(unknown location)",
        (_, null, _) => Path!,
        (_, { } start, null) => $"{Path}:{start}",
        (_, { } start, { } end) => start == end ? $"{Path}:{start}" : $"{Path}:{start}-{end}",
    };
}
