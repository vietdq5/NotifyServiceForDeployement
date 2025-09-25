using Microsoft.AspNetCore.SignalR;
using NotifyService.Application.Services;

namespace NotifyService.Api.Hubs;

public class SendNotificationHub : Hub
{
    private readonly ILogger<SendNotificationHub> _logger;
    private readonly IRedisConnectionMappingService _connectionMapping;
    private HttpContext context => Context.GetHttpContext()!;

    public SendNotificationHub(ILogger<SendNotificationHub> logger,
    IRedisConnectionMappingService connectionMapping)
    {
        _logger = logger;
        _connectionMapping = connectionMapping;
    }

    public async Task JoinGroup(string groupName)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Client {ConnectionId} joined group {GroupName}", Context.ConnectionId, groupName);
    }

    public async Task LeaveGroup(string groupName)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, groupName);
        _logger.LogInformation("Client {ConnectionId} left group {GroupName}", Context.ConnectionId, groupName);
    }

    public override async Task OnConnectedAsync()
    {
        try
        {
            var userId = GetUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                await _connectionMapping.AddConnectionAsync(userId, Context.ConnectionId);
                await Groups.AddToGroupAsync(Context.ConnectionId, $"User_{userId}");

                _logger.LogInformation("User {UserId} connected with connection {ConnectionId}", userId, Context.ConnectionId);
            }
            else
            {
                _logger.LogWarning("Connection {ConnectionId} established without user ID", Context.ConnectionId);
            }

            await base.OnConnectedAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling connection for {ConnectionId}", Context.ConnectionId);
        }
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var userId = GetUserId();
            if (!string.IsNullOrEmpty(userId))
            {
                await _connectionMapping.RemoveConnectionAsync(userId, Context.ConnectionId);
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"User_{userId}");

                _logger.LogInformation("User {UserId} disconnected from connection {ConnectionId}", userId, Context.ConnectionId);
            }

            await base.OnDisconnectedAsync(exception);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling disconnection for {ConnectionId}", Context.ConnectionId);
        }
    }

    /// <summary>
    /// Get user ID from header or query string for testing.
    /// </summary>
    /// <returns></returns>
    private string? GetUserId()
    {
        // query string parameter
        return context?.Request.Query["userId"].FirstOrDefault();
    }
}