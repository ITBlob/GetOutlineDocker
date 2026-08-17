using System.Text.Json.Serialization;

namespace Storybloq.Reader.Core.Models;

/// <summary>A record from <c>.story/tickets/</c>.</summary>
public sealed class Ticket : StoryRecord
{
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("type")]
    public StoryEnum<TicketType> Type { get; set; }

    [JsonPropertyName("status")]
    public StoryEnum<TicketStatus> Status { get; set; }

    [JsonPropertyName("phase")]
    public string? Phase { get; set; }

    [JsonPropertyName("order")]
    public int? Order { get; set; }

    [JsonPropertyName("createdDate")]
    public string? CreatedDate { get; set; }

    [JsonPropertyName("updatedDate")]
    public string? UpdatedDate { get; set; }

    [JsonPropertyName("completedDate")]
    public string? CompletedDate { get; set; }

    [JsonPropertyName("blockedBy")]
    public List<string>? BlockedBy { get; set; }

    /// <summary>Federation references, of the form <c>node:T-123</c>.</summary>
    [JsonPropertyName("crossNodeBlockedBy")]
    public List<string>? CrossNodeBlockedBy { get; set; }

    [JsonPropertyName("parentTicket")]
    public string? ParentTicket { get; set; }

    [JsonPropertyName("assignedTo")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("claimedBySession")]
    public string? ClaimedBySession { get; set; }

    [JsonIgnore]
    public bool IsComplete => Status.Is(TicketStatus.Complete);

    [JsonIgnore]
    public bool IsInProgress => Status.Is(TicketStatus.InProgress);

    [JsonIgnore]
    public DateOnly? CreatedDateValue => StoryDate.Parse(CreatedDate);

    [JsonIgnore]
    public DateOnly? CompletedDateValue => StoryDate.Parse(CompletedDate);
}
