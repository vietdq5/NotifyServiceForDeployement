using NotifyService.Domain.Entities;

namespace NotifyService.Domain.Interfaces;
public interface INotificationRepository
{
    Task<Notification> CreateAsync(Notification notification);
    Task CreateBatchAsync(IEnumerable<Notification> notifications);
    Task UpSertBatchAsync(IEnumerable<Notification> notifications);
    Task<Notification?> GetByIdAsync(string id);
    Task<IEnumerable<Notification>> GetByUserIdAsync(string userId, int limit = 50);
    Task<bool> UpdateAsync(string id, Notification notification);
    Task<bool> UpdateStatusAsync(string id, NotificationStatus status);
    Task<bool> MarkAsReadAsync(string id);
    Task<long> GetUnreadCountAsync(string userId);
    Task<IEnumerable<Notification>> GetPendingNotificationsAsync(int limit = 100);
}