using MongoDB.Driver;
using NotifyService.Domain.Entities;
using NotifyService.Domain.Interfaces;

namespace NotifyService.Infrastructure.Data.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly MongoDbContext _context;

    public NotificationRepository(MongoDbContext context)
    {
        _context = context;
    }

    public async Task<Notification> CreateAsync(Notification notification)
    {
        await _context.Notifications.InsertOneAsync(notification);
        return notification;
    }

    public async Task CreateBatchAsync(IEnumerable<Notification> notifications)
    {
        if (!notifications.Any())
        {
            return;
        }
        await _context.Notifications.InsertManyAsync(notifications);
    }

    public async Task UpSertBatchAsync(IEnumerable<Notification> notifications)
    {
        var operations = new List<WriteModel<Notification>>();
        var dbDict = new Dictionary<(string? UserId, string? PostId), Notification>();

        // get list notifications by UserId and PostId
        var keyPairs = notifications.Select(n => new { n.UserId, n.PostId }).Distinct().ToList();
        // define filter
        var filters = new List<FilterDefinition<Notification>>();

        foreach (var pair in keyPairs)
        {
            if (pair.PostId == null || pair.UserId == null) continue;
            var filter = Builders<Notification>.Filter.And(
                Builders<Notification>.Filter.Eq(n => n.UserId, pair.UserId),
                Builders<Notification>.Filter.Eq(n => n.PostId, pair.PostId)
            );
            filters.Add(filter);
        }
        if (filters.Count != 0)
        {
            var finalFilter = Builders<Notification>.Filter.Or(filters);
            // get existing notifications
            var dbNotifications = await _context.Notifications.Find(finalFilter).ToListAsync();
            dbDict = dbNotifications.ToDictionary(n => (n.UserId, n.PostId), n => n);
        }

        foreach (var notify in notifications)
        {
            var key = (notify.UserId, notify.PostId);
            var actor = notify.Actors.FirstOrDefault();
            if (actor == null) continue;
            if (dbDict.TryGetValue(key, out var existingNotify))
            {
                var filter = Builders<Notification>.Filter.And(
                    Builders<Notification>.Filter.Eq(n => n.UserId, notify.UserId),
                    Builders<Notification>.Filter.Eq(n => n.PostId, notify.PostId)
                );

                var actorExists = existingNotify.Actors.Any(a => a.UserId == actor.UserId);

                if (actor.Action == "un-react" && actorExists)
                {
                    // Remove actor if exists
                    var update = Builders<Notification>.Update.PullFilter(n => n.Actors, a => a.UserId == actor.UserId);
                    operations.Add(new UpdateOneModel<Notification>(filter, update));
                }
                else if (actor.Action != "un-react" && !actorExists)
                {
                    // add actor if not existing
                    var update = Builders<Notification>.Update.AddToSet(n => n.Actors, actor);
                    operations.Add(new UpdateOneModel<Notification>(filter, update) { IsUpsert = true });
                }
            }
            else
            {
                // insert new notification
                var insertModel = new InsertOneModel<Notification>(notify);
                operations.Add(insertModel);
            }
        }

        if (operations.Count > 0)
        {
            await _context.Notifications.BulkWriteAsync(operations);
        }
    }

    public async Task<Notification?> GetByIdAsync(string id)
    {
        var filter = Builders<Notification>.Filter.Eq(x => x.Id, id);
        return await _context.Notifications.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<IEnumerable<Notification>> GetByUserIdAsync(string userId, int limit = 50)
    {
        var filter = Builders<Notification>.Filter.Eq(x => x.UserId, userId);
        var sort = Builders<Notification>.Sort.Descending(x => x.CreatedAt);

        return await _context.Notifications
            .Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync();
    }

    public async Task<bool> UpdateAsync(string id, Notification notification)
    {
        var filter = Builders<Notification>.Filter.Eq(x => x.Id, id);
        var result = await _context.Notifications.ReplaceOneAsync(filter, notification);
        return result.IsAcknowledged && result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateStatusAsync(string id, NotificationStatus status)
    {
        var filter = Builders<Notification>.Filter.Eq(x => x.Id, id);
        var update = Builders<Notification>.Update
            .Set(x => x.Status, status)
            .Set(x => x.ProcessedAt, DateTime.UtcNow);

        var result = await _context.Notifications.UpdateOneAsync(filter, update);
        return result.IsAcknowledged && result.ModifiedCount > 0;
    }

    public async Task<bool> MarkAsReadAsync(string id)
    {
        var filter = Builders<Notification>.Filter.Eq(x => x.Id, id);
        var update = Builders<Notification>.Update
            .Set(x => x.Status, NotificationStatus.Read)
            .Set(x => x.ReadAt, DateTime.UtcNow);

        var result = await _context.Notifications.UpdateOneAsync(filter, update);
        return result.IsAcknowledged && result.ModifiedCount > 0;
    }

    public async Task<long> GetUnreadCountAsync(string userId)
    {
        var filter = Builders<Notification>.Filter.And(
            Builders<Notification>.Filter.Eq(x => x.UserId, userId),
            Builders<Notification>.Filter.Ne(x => x.Status, NotificationStatus.Read)
        );

        return await _context.Notifications.CountDocumentsAsync(filter);
    }

    public async Task<IEnumerable<Notification>> GetPendingNotificationsAsync(int limit = 100)
    {
        var filter = Builders<Notification>.Filter.Eq(x => x.Status, NotificationStatus.Pending);
        var sort = Builders<Notification>.Sort.Ascending(x => x.CreatedAt);

        return await _context.Notifications
            .Find(filter)
            .Sort(sort)
            .Limit(limit)
            .ToListAsync();
    }
}