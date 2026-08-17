using System.Text.Json.Serialization;

namespace Storybloq.Reader.Core.Models;

/// <summary>A record from <c>.story/notes/</c>. Title is nullable by design upstream.</summary>
public sealed class Note : StoryRecord
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("status")]
    public StoryEnum<NoteStatus> Status { get; set; }

    [JsonPropertyName("createdDate")]
    public string? CreatedDate { get; set; }

    [JsonPropertyName("updatedDate")]
    public string? UpdatedDate { get; set; }

    /// <summary>Notes may be untitled; fall back to the first line of the body.</summary>
    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Title))
            {
                return Title;
            }

            if (string.IsNullOrWhiteSpace(Content))
            {
                return Label;
            }

            var firstLine = Content.AsSpan();
            var breakIndex = firstLine.IndexOfAny('\r', '\n');
            var line = (breakIndex >= 0 ? firstLine[..breakIndex] : firstLine).Trim().ToString();
            return line.Length <= 80 ? line : line[..77] + "...";
        }
    }
}
