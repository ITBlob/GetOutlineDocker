using System.Text.Json;
using System.Text.Json.Serialization;

namespace Storybloq.Reader.Core.Models;

/// <summary><c>.story/roadmap.json</c> — phase ordering plus project-level blockers.</summary>
public sealed class Roadmap
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("phases")]
    public List<Phase>? Phases { get; set; }

    [JsonPropertyName("blockers")]
    public List<Blocker>? Blockers { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public IReadOnlyList<Phase> PhaseList => Phases ?? [];

    [JsonIgnore]
    public IReadOnlyList<Blocker> BlockerList => Blockers ?? [];
}

public sealed class Phase
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("label")]
    public string? Label { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public string DisplayName => Name ?? Label ?? Id ?? "(unnamed phase)";
}

public sealed class Blocker
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Legacy pre-T-082 flag, superseded by <see cref="ClearedDate"/>.</summary>
    [JsonPropertyName("cleared")]
    public bool? Cleared { get; set; }

    [JsonPropertyName("createdDate")]
    public string? CreatedDate { get; set; }

    [JsonPropertyName("clearedDate")]
    public string? ClearedDate { get; set; }

    [JsonPropertyName("note")]
    public string? Note { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    /// <summary>
    /// Upstream migrated blockers from a <c>cleared</c> boolean to a <c>clearedDate</c> in T-082,
    /// and both shapes survive in the wild. A date wins when present; otherwise fall back to the
    /// legacy flag, and treat a blocker with neither as still blocking.
    /// </summary>
    [JsonIgnore]
    public bool IsCleared => !string.IsNullOrWhiteSpace(ClearedDate) || Cleared == true;
}
