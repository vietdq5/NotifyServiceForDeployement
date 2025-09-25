using Microsoft.Extensions.Options;
using NotifyService.Configurations;
using NotifyService.Domain.Interfaces;
using RabbitMQ.Client;

namespace NotifyService.Infrastructure.Messaging;

public class RabbitMqConnectionFactory : IRabbitMqConnectionFactory, IDisposable
{
    private readonly RabbitMQSetting _rabbitMqSetting;
    private IConnection? _connection;
    private readonly object _lock = new();

    public RabbitMqConnectionFactory(IOptions<RabbitMQSetting> options)
    {
        _rabbitMqSetting = options.Value;
        InitConnection();
    }

    private void InitConnection()
    {
        var factory = new ConnectionFactory
        {
            HostName = _rabbitMqSetting.HostName,
            Port = _rabbitMqSetting.Port,
            UserName = _rabbitMqSetting.UserName,
            Password = _rabbitMqSetting.Password,
            VirtualHost = _rabbitMqSetting.VirtualHost,
            DispatchConsumersAsync = true,
            AutomaticRecoveryEnabled = true,
            NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
        };

        _connection = factory.CreateConnection();
    }

    public IConnection Connection
    {
        get
        {
            if (_connection == null || !_connection.IsOpen)
            {
                lock (_lock)
                {
                    if (_connection == null || !_connection.IsOpen)
                    {
                        InitConnection();
                    }
                }
            }

            return _connection!;
        }
    }

    public IModel CreateChannel(IConnection connection)
    {
        return connection.CreateModel();
    }

    public bool IsConnected => _connection?.IsOpen == true;

    public void Dispose()
    {
        _connection?.Close();
        _connection?.Dispose();
    }
}