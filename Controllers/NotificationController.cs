using Microsoft.AspNetCore.Mvc;
using NotifyService.Converters;
using NotifyService.Domain.Entities;
using NotifyService.Domain.Interfaces;
using System.Text.Json.Serialization;

namespace NotifyService.Controllers;

[ApiController]
[Route("api/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly INotificationRepository _repository;
    private readonly IMessagePublisher _publisher;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(
        INotificationRepository repository,
        IMessagePublisher publisher,
        ILogger<NotificationController> logger)
    {
        _repository = repository;
        _publisher = publisher;
        _logger = logger;
    }

    [HttpPost]
    public async Task<ActionResult> CreateNotification([FromBody] CreateNotificationDto dto)
    {
        try
        {
            var notification = new Notification
            {
                UserId = dto.UserId,
                Title = dto.Title,
                Message = dto.Message,
                Metadata = dto.Metadata,
                CreatedAt = DateTime.UtcNow,
                Type = dto.Type,
                IsRead = false,
                Actors = new List<NotificationActor>
                {
                    new NotificationActor
                    {
                        UserId = dto.SenderId,
                        FullName = dto.SenderFullName
                    }
                },
            };
            // Publish to RabbitMQ for async processing
            await _publisher.PublishAsync(notification);

            _logger.LogInformation("Notification published to queue for user {UserId}", dto.UserId);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating notification");
            return StatusCode(500, new { error = "Failed to create notification" });
        }
    }

    [HttpPost("batch")]
    public async Task<ActionResult> CreateBatchNotifications([FromBody] List<CreateNotificationDto> notifications)
    {
        try
        {
            await _publisher.PublishBatchAsync(notifications);

            _logger.LogInformation("Batch of {Count} notifications published to queue", notifications.Count);

            return Accepted(new { count = notifications.Count, status = "queued" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating batch notifications");
            return StatusCode(500, new { error = "Failed to create batch notifications" });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<Notification>> GetNotification(string id)
    {
        var notification = await _repository.GetByIdAsync(id);
        if (notification == null)
        {
            return NotFound();
        }

        return Ok(notification);
    }

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<IEnumerable<Notification>>> GetUserNotifications(
        string userId,
        [FromQuery] int limit = 50)
    {
        var notifications = await _repository.GetByUserIdAsync(userId, limit);
        return Ok(notifications);
    }

    [HttpGet("user/{userId}/unread-count")]
    public async Task<ActionResult<long>> GetUnreadCount(string userId)
    {
        var count = await _repository.GetUnreadCountAsync(userId);
        return Ok(new { userId, unreadCount = count });
    }

    [HttpPut("{id}/read")]
    public async Task<ActionResult> MarkAsRead(string id)
    {
        var success = await _repository.MarkAsReadAsync(id);
        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPut("{id}/status")]
    public async Task<ActionResult> UpdateStatus(string id, [FromBody] UpdateStatusDto dto)
    {
        var success = await _repository.UpdateStatusAsync(id, dto.Status);
        if (!success)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpGet("pending")]
    public async Task<ActionResult<IEnumerable<Notification>>> GetPendingNotifications(
        [FromQuery] int limit = 100)
    {
        var notifications = await _repository.GetPendingNotificationsAsync(limit);
        return Ok(notifications);
    }
}

// DTO Models
public class CreateNotificationDto
{
    public string UserId { get; set; } = string.Empty;
    public string SenderId { get; set; } = string.Empty;
    public string SenderFullName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public NotificationType NotificationType { get; set; } = NotificationType.Empty;
    [JsonConverter(typeof(DictionaryObjectJsonConverter))]
    public Dictionary<string, object?>? Metadata { get; set; }
}

public class UpdateStatusDto
{
    public NotificationStatus Status { get; set; }
}