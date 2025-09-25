namespace NotifyService.Configurations;

public class MongoDbSetting
{
    public string ConnectionString { get; set; } = "mongodb://localhost:27017";
    public string DatabaseName { get; set; } = "NotificationDB";
    public string NotificationsCollection { get; set; } = "notifications";
}
