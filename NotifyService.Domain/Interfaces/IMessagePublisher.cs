using RabbitMQ.Client;

namespace NotifyService.Domain.Interfaces;

public interface IMessagePublisher
{
    Task PublishAsync<T>(T message, string routingKey = "") where T : class;
    Task PublishBatchAsync<T>(IEnumerable<T> messages, string routingKey = "") where T : class;
}