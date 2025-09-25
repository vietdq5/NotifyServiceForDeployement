using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace NotifyService.Domain.Entities;

public class Notification
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("postId")]
    public string? PostId { get; set; }

    /// <summary>
    /// User who will receive the notification
    /// </summary>
    [BsonElement("userId")]
    public string? UserId { get; set; }

    [BsonElement("notificationType")]
    public NotificationType NotificationType { get; set; }

    [BsonElement("count")]
    public int Count { get; set; }

    /// <summary>
    /// List of users who triggered the notification event: Other User, Page, Group, System, etc.
    /// </summary>
    [BsonElement("actors")]
    public List<NotificationActor> Actors { get; set; } = new();

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("message")]
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// event type, e.g., "PostCreate", "UserReact"
    /// </summary>
    [BsonElement("type")]
    public string Type { get; set; } = string.Empty;

    [BsonElement("status")]
    public NotificationStatus Status { get; set; } = NotificationStatus.Pending;

    [BsonElement("metadata")]
    public Dictionary<string, object?>? Metadata { get; set; }

    [BsonElement("createdAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("processedAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ProcessedAt { get; set; }

    [BsonElement("isSeen")]
    public bool IsSeen { get; set; }

    [BsonElement("seenAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? SeenAt { get; set; }

    [BsonElement("isRead")]
    public bool IsRead { get; set; }

    [BsonElement("readAt")]
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime? ReadAt { get; set; }

    [BsonElement("retryCount")]
    public int RetryCount { get; set; } = 0;

    [BsonElement("error")]
    public string? Error { get; set; }

    [BsonIgnore]
    public ulong DeliveryTag { get; set; }
}

public class NotificationActor
{
    [BsonElement("userId")]
    public string UserId { get; set; } = string.Empty;

    [BsonElement("fullName")]
    public string FullName { get; set; } = string.Empty;

    [BsonElement("avatarUrl")]
    public string AvatarUrl { get; set; } = string.Empty;

    [BsonElement("action")]
    public string Action { get; set; } = string.Empty;

    [BsonElement("interactedAt")]
    public DateTime InteractedAt { get; set; } = DateTime.UtcNow;
}

public enum NotificationStatus
{
    Pending = 0,
    Processing = 1,
    Sent = 2,
    Read = 3,
    Failed = 4,
    Cancelled = 5
}

public enum NotificationType
{
    Empty,
    User,
    Page,
    Group,
    System
}