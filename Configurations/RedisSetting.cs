namespace NotifyService.Configurations;

public class RedisSetting
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public int Database { get; set; } = 0;
    public string KeyPrefix { get; set; } = "notify:";
}