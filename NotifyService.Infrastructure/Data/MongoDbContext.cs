using MongoDB.Driver;
using NotifyService.Domain.Entities;

namespace NotifyService.Infrastructure.Data;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;
    private readonly IMongoClient _client;

    public MongoDbContext(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("MongoDB")
            ?? throw new ArgumentNullException("MongoDB connection string is missing");

        var databaseName = configuration["MongoDB:DatabaseName"] ?? "NotifyDB";

        _client = new MongoClient(connectionString);
        _database = _client.GetDatabase(databaseName);

        // Create indexes
        CreateIndexes();
    }

    public IMongoCollection<Notification> Notifications =>
        _database.GetCollection<Notification>("notifications");

    private void CreateIndexes()
    {
        var indexKeysDefinition = Builders<Notification>.IndexKeys.Ascending(x => x.UserId).Descending(x => x.CreatedAt);

        Notifications.Indexes.CreateOne(new CreateIndexModel<Notification>(indexKeysDefinition, new CreateIndexOptions { Name = "userId_createdAt" }));

        // Index for PostId and UserId
        var indexKeysDefinitionPost = Builders<Notification>.IndexKeys.Ascending(x => x.PostId).Descending(x => x.UserId).Descending(x => x.CreatedAt);

        Notifications.Indexes.CreateOne(new CreateIndexModel<Notification>(indexKeysDefinitionPost, new CreateIndexOptions { Name = "postId_userId_createdAt" }));

        // Index for status
        var statusIndex = Builders<Notification>.IndexKeys.Ascending(x => x.Status);
        Notifications.Indexes.CreateOne(new CreateIndexModel<Notification>(statusIndex, new CreateIndexOptions { Name = "status" }));
    }
}