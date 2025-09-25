using StackExchange.Redis;

namespace NotifyService.Application.Services;

public interface IRedisConnectionMappingService
{
    Task AddConnectionAsync(string userId, string connectionId);
    Task RemoveConnectionAsync(string userId, string connectionId);
    Task<List<string>> GetConnectionsAsync(string userId);
    Task<List<string>> GetAllUsersAsync();
    Task<bool> IsUserConnectedAsync(string userId);
    Task<int> GetConnectedUsersCountAsync();
    Task RemoveAllConnectionsForUserAsync(string userId);
}

public class RedisConnectionMappingService : IRedisConnectionMappingService
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IDatabase _database;
    private readonly ILogger<RedisConnectionMappingService> _logger;
    private const string USER_CONNECTIONS_PREFIX = "signalr:user_connections:";
    private const string CONNECTION_USER_PREFIX = "signalr:connection_user:";
    private const string CONNECTED_USERS_SET = "signalr:connected_users";

    public RedisConnectionMappingService(
        IConnectionMultiplexer redis,
        ILogger<RedisConnectionMappingService> logger)
    {
        _redis = redis;
        _database = redis.GetDatabase();
        _logger = logger;
    }

    public async Task AddConnectionAsync(string userId, string connectionId)
    {
        try
        {
            var userConnectionsKey = USER_CONNECTIONS_PREFIX + userId;
            var connectionUserKey = CONNECTION_USER_PREFIX + connectionId;

            // Add connection to user's connection set
            await _database.SetAddAsync(userConnectionsKey, connectionId);

            // Map connection back to user
            await _database.StringSetAsync(connectionUserKey, userId);

            // Add user to connected users set
            await _database.SetAddAsync(CONNECTED_USERS_SET, userId);

            // Set expiration for cleanup (optional, 24 hours)
            await _database.KeyExpireAsync(userConnectionsKey, TimeSpan.FromHours(24));
            await _database.KeyExpireAsync(connectionUserKey, TimeSpan.FromHours(24));

            _logger.LogInformation("Added connection {ConnectionId} for user {UserId}", connectionId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding connection {ConnectionId} for user {UserId}", connectionId, userId);
            throw;
        }
    }

    public async Task RemoveConnectionAsync(string userId, string connectionId)
    {
        try
        {
            var userConnectionsKey = USER_CONNECTIONS_PREFIX + userId;
            var connectionUserKey = CONNECTION_USER_PREFIX + connectionId;

            // Remove connection from user's connection set
            await _database.SetRemoveAsync(userConnectionsKey, connectionId);

            // Remove connection-user mapping
            await _database.KeyDeleteAsync(connectionUserKey);

            // Check if user has any more connections
            var remainingConnections = await _database.SetLengthAsync(userConnectionsKey);
            if (remainingConnections == 0)
            {
                // Remove user from connected users set if no connections remain
                await _database.SetRemoveAsync(CONNECTED_USERS_SET, userId);
                await _database.KeyDeleteAsync(userConnectionsKey);
            }

            _logger.LogInformation("Removed connection {ConnectionId} for user {UserId}", connectionId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing connection {ConnectionId} for user {UserId}", connectionId, userId);
            throw;
        }
    }

    public async Task<List<string>> GetConnectionsAsync(string userId)
    {
        try
        {
            var userConnectionsKey = USER_CONNECTIONS_PREFIX + userId;
            var connections = await _database.SetMembersAsync(userConnectionsKey);
            return connections.Select(c => c.ToString()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting connections for user {UserId}", userId);
            return new List<string>();
        }
    }

    public async Task<List<string>> GetAllUsersAsync()
    {
        try
        {
            var users = await _database.SetMembersAsync(CONNECTED_USERS_SET);
            return users.Select(u => u.ToString()).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all connected users");
            return new List<string>();
        }
    }

    public async Task<bool> IsUserConnectedAsync(string userId)
    {
        try
        {
            return await _database.SetContainsAsync(CONNECTED_USERS_SET, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if user {UserId} is connected", userId);
            return false;
        }
    }

    public async Task<int> GetConnectedUsersCountAsync()
    {
        try
        {
            return (int)await _database.SetLengthAsync(CONNECTED_USERS_SET);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting connected users count");
            return 0;
        }
    }

    public async Task RemoveAllConnectionsForUserAsync(string userId)
    {
        try
        {
            var userConnectionsKey = USER_CONNECTIONS_PREFIX + userId;
            var connections = await GetConnectionsAsync(userId);

            foreach (var connectionId in connections)
            {
                var connectionUserKey = CONNECTION_USER_PREFIX + connectionId;
                await _database.KeyDeleteAsync(connectionUserKey);
            }

            await _database.KeyDeleteAsync(userConnectionsKey);
            await _database.SetRemoveAsync(CONNECTED_USERS_SET, userId);

            _logger.LogInformation("Removed all connections for user {UserId}", userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing all connections for user {UserId}", userId);
            throw;
        }
    }
}