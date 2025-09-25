using Microsoft.AspNetCore.SignalR;
using NotifyService.Api.Hubs;
using NotifyService.Domain.Entities;
namespace NotifyService.Application.Services;

public interface ISignalRNotificationService
{
    Task SendReactNotificationAsync(string userId, string senderFullName, string title, CancellationToken cancellationToken = default);
    Task SendTagUserNotificationAsync(List<string> userIds, string senderFullName, string title, CancellationToken cancellationToken = default);
    Task SendAllConnections(Notification notification, CancellationToken cancellationToken = default);
    Task<List<string>> GetConnectedUsersAsync();
    Task<bool> IsUserConnectedAsync(string userId);
    Task<int> GetConnectedUsersCountAsync();
}

public class SignalRNotificationService : ISignalRNotificationService
{
    private readonly IHubContext<SendNotificationHub> _hubContext;
    private readonly IRedisConnectionMappingService _connectionMapping;
    private readonly ILogger<SignalRNotificationService> _logger;

    public SignalRNotificationService(
        IHubContext<SendNotificationHub> hubContext,
        IRedisConnectionMappingService connectionMapping,
        ILogger<SignalRNotificationService> logger)
    {
        _hubContext = hubContext;
        _connectionMapping = connectionMapping;
        _logger = logger;
    }

    /// <summary>
    /// send notification to user if user online
    /// </summary>
    /// <param name="userId"></param>
    /// <param name="senderFullName"></param>
    /// <param name="title"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task SendReactNotificationAsync(string userId, string senderFullName, string title, CancellationToken cancellationToken = default)
    {
        try
        {
            var notification = new
            {
                UserId = userId,
                SenderFullName = senderFullName,
                Title = title,
                Type = "react",
                CreatedAt = DateTime.UtcNow
            };
            var connections = await _connectionMapping.GetConnectionsAsync(userId);
            if (connections.Count != 0)
            {
                await _hubContext.Clients.Clients(connections).SendAsync("ReceiveNotification", notification);
                // send all for debugging
                //await _hubContext.Clients.All.SendAsync("ReceiveNotification", notification, cancellationToken);
                _logger.LogInformation("Sent notification to user {UserId} on {ConnectionCount} connections", userId, connections.Count);
            }
            else
            {
                _logger.LogWarning("User {UserId} has no active connections", userId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification to user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// send all users if user online
    /// </summary>
    /// <param name="userIds"></param>
    /// <param name="notification"></param>
    /// <returns></returns>
    public async Task SendTagUserNotificationAsync(List<string> userIds, string senderFullName, string title, CancellationToken cancellationToken = default)
    {
        try
        {
            var allConnections = new List<string>();
            foreach (var userId in userIds)
            {
                var connections = await _connectionMapping.GetConnectionsAsync(userId);
                allConnections.AddRange(connections);
            }
            var notification = new
            {
                SenderFullName = senderFullName,
                Title = title,
                Type = "Tag",
                CreatedAt = DateTime.UtcNow
            };
            if (allConnections.Count != 0)
            {
                await _hubContext.Clients.Clients(allConnections).SendAsync("ReceiveNotification", notification, cancellationToken);
                _logger.LogInformation("Sent notification to {UserCount} users on {ConnectionCount} connections", userIds.Count, allConnections.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending notification to multiple users");
            throw;
        }
    }
    public async Task<List<string>> GetConnectedUsersAsync()
    {
        return await _connectionMapping.GetAllUsersAsync();
    }

    public async Task<bool> IsUserConnectedAsync(string userId)
    {
        return await _connectionMapping.IsUserConnectedAsync(userId);
    }

    public async Task<int> GetConnectedUsersCountAsync()
    {
        return await _connectionMapping.GetConnectedUsersCountAsync();
    }

    public async Task SendAllConnections(Notification notification, CancellationToken cancellationToken = default)
    {
        var notificationMessage = new
        {
            Type = "TodoCreated",
            Message = $"New todo created: {notification.Message ?? "No Description"}",
            Data = new
            {
                Id = notification.PostId,
                Title = notification.Title,
                CreatedAt = DateTime.UtcNow
            }
        };
        await _hubContext.Clients.All.SendAsync("ReceiveNotification", notificationMessage, cancellationToken);
        _logger.LogInformation("Sent todo created notification for {TodoId}", notification.Id);
    }
}