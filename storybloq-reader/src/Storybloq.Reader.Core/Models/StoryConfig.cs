using System.Text.Json;
using System.Text.Json.Serialization;

namespace Storybloq.Reader.Core.Models;

/// <summary>
/// <c>.story/config.json</c> — the file whose presence defines a project root.
/// </summary>
public sealed class StoryConfig
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("project")]
    public string? Project { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("language")]
    public string? Language { get; set; }

    [JsonPropertyName("features")]
    public StoryFeatures? Features { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public StoryFeatures EffectiveFeatures => Features ?? StoryFeatures.AllEnabled;
}

/// <summary>
/// Feature flags. Upstream's schema comment notes the passthrough exists so future fields do
/// not break older readers, so an absent flag is treated as enabled rather than disabled —
/// hiding a panel the user actually has data for is the worse failure.
/// </summary>
public sealed class StoryFeatures
{
    public static StoryFeatures AllEnabled { get; } = new();

    [JsonPropertyName("tickets")]
    public bool? Tickets { get; set; }

    [JsonPropertyName("issues")]
    public bool? Issues { get; set; }

    [JsonPropertyName("handovers")]
    public bool? Handovers { get; set; }

    [JsonPropertyName("roadmap")]
    public bool? Roadmap { get; set; }

    [JsonPropertyName("reviews")]
    public bool? Reviews { get; set; }

    [JsonPropertyName("bus")]
    public bool? Bus { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public bool TicketsEnabled => Tickets != false;

    [JsonIgnore]
    public bool IssuesEnabled => Issues != false;

    [JsonIgnore]
    public bool HandoversEnabled => Handovers != false;

    [JsonIgnore]
    public bool RoadmapEnabled => Roadmap != false;
}
