using RabbitMQ.Client;

namespace NotifyService.Domain.Interfaces;

public interface IRabbitMqConnectionFactory
{
    IConnection Connection { get; }
    bool IsConnected { get; }
}
