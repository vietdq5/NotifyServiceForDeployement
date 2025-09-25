using Microsoft.Extensions.Options;
using NotifyService.Application.Services;
using NotifyService.Configurations;
using NotifyService.Converters;
using NotifyService.Domain.Entities;
using NotifyService.Domain.Interfaces;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;
using System.Text.Json;

namespace NotifyService.Infrastructure.Workers;

public class NotifyConsumerWorker : BackgroundService
{
    private readonly ILogger<NotifyConsumerWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMQSetting _config;
    private readonly JsonSerializerOptions _jsonSerializerOptions;
    private readonly IRabbitMqConnectionFactory _rabbitMqConnectionFactory;
    private IConnection? _connection;
    private IModel? _channel;

    public NotifyConsumerWorker(
        ILogger<NotifyConsumerWorker> logger,
        IServiceProvider serviceProvider,
        IOptions<RabbitMQSetting> options,
        IRabbitMqConnectionFactory rabbitMqConnectionFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = options.Value;
        _jsonSerializerOptions = new JsonSerializerOptions()
        {
            Converters = { new DictionaryObjectJsonConverter() }
        };
        _rabbitMqConnectionFactory = rabbitMqConnectionFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Message Consumer Worker starting...");
        // init rabbitmq connection
        await InitializeRabbitMQ(cancellationToken);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(1000, cancellationToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Message Consumer Worker stopping...");
    }

    private async Task InitializeRabbitMQ(CancellationToken stoppingToken)
    {
        var retryCount = 0;
        const int maxRetries = 5;

        while (retryCount < maxRetries && !stoppingToken.IsCancellationRequested)
        {
            try
            {
                _connection ??= _rabbitMqConnectionFactory.Connection;
                if (_channel == null)
                {
                    _channel = _connection.CreateModel();
                    // Set QoS to control message prefetch
                    _channel.BasicQos(prefetchSize: 0, prefetchCount: 10, global: false);
                    // Declare exchange
                    _channel.ExchangeDeclare(exchange: _config.ExchangeName, type: ExchangeType.Topic, durable: true, autoDelete: false);

                    // Declare queue
                    _channel.QueueDeclare(queue: _config.NotifyUserQueue, durable: true, exclusive: false, autoDelete: false);

                    // Bind queue to exchange
                    _channel.QueueBind(queue: _config.NotifyUserQueue, exchange: _config.ExchangeName, routingKey: _config.RoutingKeySendNotify);
                }

                // Create consumer
                var consumer = new AsyncEventingBasicConsumer(_channel);
                consumer.Received += async (model, ea) =>
                {
                    await ProcessMessageAsync(ea, stoppingToken);
                };

                consumer.ConsumerCancelled += async (model, ea) =>
                {
                    _logger.LogWarning("Consumer cancelled, attempting to reconnect...");
                    await Task.Delay(5000, stoppingToken);
                    await InitializeRabbitMQ(stoppingToken);
                };

                // Start consuming
                _channel.BasicConsume(queue: _config.NotifyUserQueue, autoAck: false, consumer: consumer);
                _logger.LogInformation("Successfully connected to RabbitMQ and started consuming messages");
                break;
            }
            catch (Exception ex)
            {
                retryCount++;
                _logger.LogError(ex, "Failed to connect to RabbitMQ. Retry {RetryCount}/{MaxRetries}", retryCount, maxRetries);

                if (retryCount < maxRetries)
                {
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), stoppingToken);
                }
                else
                {
                    throw new InvalidOperationException("Could not connect to RabbitMQ after maximum retries", ex);
                }
            }
        }
    }

    private async Task ProcessMessageAsync(BasicDeliverEventArgs ea, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Starting receive message to process.");
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            var notificationMessage = JsonSerializer.Deserialize<Notification>(message, _jsonSerializerOptions);
            if (notificationMessage != null)
            {
                using var serviceScope = _serviceProvider.CreateScope();
                var _signalRNotificationService = serviceScope.ServiceProvider.GetRequiredService<ISignalRNotificationService>();
                // var fullName = "Someone";
                // if (notificationMessage.Actors.Count == 0)
                // {
                //     fullName = "Someone";
                // }
                // else
                // // one user
                // if (notificationMessage.Actors.Count == 1)
                // {
                //     fullName = notificationMessage.Actors[0].FullName;
                // }
                // else
                // {
                //     fullName = notificationMessage.Actors.OrderBy(s => s.InteractedAt).FirstOrDefault()?.FullName + " and others";
                // }
                await _signalRNotificationService.SendAllConnections(notificationMessage, cancellationToken);
                _channel?.BasicAck(ea.DeliveryTag, false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            // Reject message and don't requeue
            _channel?.BasicNack(ea.DeliveryTag, false, false);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Message Consumer Worker is stopping...");
        _channel?.Close();

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}