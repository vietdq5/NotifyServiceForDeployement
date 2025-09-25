using Microsoft.Extensions.Options;
using NotifyService.Configurations;
using NotifyService.Domain.Interfaces;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;

namespace NotifyService.Infrastructure.Messaging;

public class RabbitMQPublisher : IMessagePublisher, IDisposable
{
    private readonly RabbitMQSetting _config;
    private readonly IConnection _connection;
    private readonly IModel _channel;

    public RabbitMQPublisher(IOptions<RabbitMQSetting> options, IRabbitMqConnectionFactory rabbitMqConnectionFactory)
    {
        _config = options.Value;
        _connection ??= rabbitMqConnectionFactory.Connection;
        _channel = _connection.CreateModel();

        // Declare exchange
        _channel.ExchangeDeclare(exchange: _config.ExchangeName, type: ExchangeType.Topic, durable: true, autoDelete: false);

        // Declare queue
        _channel.QueueDeclare(queue: _config.QueueName, durable: true, exclusive: false, autoDelete: false);

        // Bind queue to exchange
        _channel.QueueBind(queue: _config.QueueName, exchange: _config.ExchangeName, routingKey: _config.RoutingKey);
    }

    public async Task PublishAsync<T>(T message, string routingKey = "") where T : class
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = _channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";

        _channel.BasicPublish(
            exchange: _config.ExchangeName,
            routingKey: string.IsNullOrEmpty(routingKey) ? _config.RoutingKey : routingKey,
            basicProperties: properties,
            body: body);

        await Task.CompletedTask;
    }

    public async Task PublishBatchAsync<T>(IEnumerable<T> messages, string routingKey = "") where T : class
    {
        foreach (var message in messages)
        {
            await PublishAsync(message, routingKey);
        }
    }

    public void Dispose()
    {
        _channel?.Close();
        _channel?.Dispose();
    }
}