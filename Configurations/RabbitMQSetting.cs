namespace NotifyService.Configurations;

public class RabbitMQSetting
{
    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";
    public string ExchangeName { get; set; } = "notifications.exchange";
    public string QueueName { get; set; } = "notifications.queue";
    public string RoutingKey { get; set; } = "notification.created";
    public string NotifyUserQueue { get; set; } = "notifications.user.queue";
    public string RoutingKeySendNotify { get; set; } = "notifications.user.queue";
    public int PrefetchCount { get; set; } = 10;
    public int BatchSize { get; set; } = 10;
    public int BatchTimeout { get; set; } = 5000; // milliseconds
}
