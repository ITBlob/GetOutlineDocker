using System.Text.Json.Serialization;

namespace Storybloq.Reader.Core.Models;

/// <summary>A record from <c>.story/lessons/</c>.</summary>
public sealed class Lesson : StoryRecord
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("context")]
    public string? Context { get; set; }

    [JsonPropertyName("source")]
    public StoryEnum<LessonSource> Source { get; set; }

    [JsonPropertyName("status")]
    public StoryEnum<LessonStatus> Status { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    /// <summary>How many times this lesson has been re-learned. Upstream's signal of weight.</summary>
    [JsonPropertyName("reinforcements")]
    public int? Reinforcements { get; set; }

    [JsonPropertyName("lastValidated")]
    public string? LastValidated { get; set; }

    [JsonPropertyName("createdDate")]
    public string? CreatedDate { get; set; }

    [JsonPropertyName("updatedDate")]
    public string? UpdatedDate { get; set; }

    /// <summary>The lesson this one replaces, if any.</summary>
    [JsonPropertyName("supersedes")]
    public string? Supersedes { get; set; }

    [JsonIgnore]
    public bool IsCurrent => !Status.Is(LessonStatus.Deprecated) && !Status.Is(LessonStatus.Superseded);

    [JsonIgnore]
    public DateOnly? LastValidatedValue => StoryDate.Parse(LastValidated);
}
