using Microsoft.Extensions.Options;
using NotifyService.Configurations;
using NotifyService.Converters;
using NotifyService.Domain.Entities;
using NotifyService.Domain.Interfaces;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace NotifyService.Infrastructure.Workers;

public class MessageConsumerWorker : BackgroundService
{
    private readonly ILogger<MessageConsumerWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly RabbitMQSetting _config;
    private readonly IRabbitMqConnectionFactory _rabbitMqConnectionFactory;

    private readonly ConcurrentQueue<Notification> _messageQueue;
    private readonly SemaphoreSlim _batchSemaphore;
    private DateTime _lastBatchProcess;
    private readonly INotificationRepository _notificationRepository;
    private readonly JsonSerializerOptions _jsonSerializerOptions;

    private IConnection? _connection;
    private IModel? _channel;

    public MessageConsumerWorker(
        ILogger<MessageConsumerWorker> logger,
        IServiceProvider serviceProvider,
        IOptions<RabbitMQSetting> options,
        INotificationRepository notificationRepository,
        IRabbitMqConnectionFactory rabbitMqConnectionFactory)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _config = options.Value;
        _messageQueue = new ConcurrentQueue<Notification>();
        _batchSemaphore = new SemaphoreSlim(1, 1);
        _lastBatchProcess = DateTime.UtcNow;
        _notificationRepository = notificationRepository;
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
        // Start batch processor task
        _ = Task.Run(async () => await BatchProcessorAsync(cancellationToken), cancellationToken);
        // Keep the service running
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
                    _channel.BasicQos(prefetchSize: 0, prefetchCount: (ushort)_config.PrefetchCount, global: false);
                    // Declare exchange
                    _channel.ExchangeDeclare(exchange: _config.ExchangeName, type: ExchangeType.Topic, durable: true, autoDelete: false);

                    // Declare queue
                    _channel.QueueDeclare(queue: _config.QueueName, durable: true, exclusive: false, autoDelete: false);
                    _channel.QueueDeclare(queue: _config.NotifyUserQueue, durable: true, exclusive: false, autoDelete: false);

                    // Bind queue to exchange
                    _channel.QueueBind(queue: _config.QueueName, exchange: _config.ExchangeName, routingKey: _config.RoutingKey);
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
                _channel.BasicConsume(queue: _config.QueueName, autoAck: false, consumer: consumer);
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
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);
            var eventType = Encoding.UTF8.GetString((byte[])ea.BasicProperties.Headers["EventType"]);
            _logger.LogInformation("Received message with EventType: {EventType}", eventType);
            var todoCreatedEvent = JsonSerializer.Deserialize<TodoCreatedEvent>(message, _jsonSerializerOptions);
            var notificationMessage = new Notification()
            {
                Title = todoCreatedEvent?.Title ?? "No Title",
                Message = todoCreatedEvent?.Description ?? "No Description",
                Type = eventType,
                PostId = todoCreatedEvent?.TodoId.ToString(),
            };
            if (notificationMessage != null)
            {
                notificationMessage.DeliveryTag = ea.DeliveryTag;
                _messageQueue.Enqueue(notificationMessage);

                _logger.LogDebug("Message queued for batch processing. Queue size: {QueueSize}", _messageQueue.Count);

                // Check if we should process batch immediately
                if (_messageQueue.Count >= _config.BatchSize)
                {
                    await ProcessBatchAsync(cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message");
            // Reject message and don't requeue
            _channel?.BasicNack(ea.DeliveryTag, false, false);
        }
    }

    private async Task BatchProcessorAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Check if timeout has passed since last batch
                var timeSinceLastBatch = DateTime.UtcNow - _lastBatchProcess;
                if (timeSinceLastBatch.TotalMilliseconds >= _config.BatchTimeout && !_messageQueue.IsEmpty)
                {
                    await ProcessBatchAsync(cancellationToken);
                }
                // Avoid tight loop
                if (_messageQueue.IsEmpty)
                {
                    await Task.Yield();
                }
                else
                {
                    await Task.Delay(500, cancellationToken);
                }
            }
            catch (TaskCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in batch processor");
            }
        }
    }

    private async Task ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await _batchSemaphore.WaitAsync(cancellationToken);
        try
        {
            if (_messageQueue.IsEmpty)
                return;

            var batch = new List<Notification>();
            var deliveryTags = new List<ulong>();

            // Dequeue up to BatchSize messages
            while (batch.Count < _config.BatchSize && _messageQueue.TryDequeue(out var message))
            {
                batch.Add(message);
                deliveryTags.Add(message.DeliveryTag);
            }

            if (batch.Count == 0)
                return;

            _logger.LogInformation("Processing batch of {BatchSize} messages", batch.Count);
            try
            {
                // Batch insert to MongoDB
                await _notificationRepository.CreateBatchAsync(batch);

                // Acknowledge all messages in batch
                foreach (var tag in deliveryTags)
                {
                    _channel?.BasicAck(tag, false);
                }
                foreach (var notification in batch)
                {
                    // avoid send notification to self
                    if (notification.Actors.Any(s => s.UserId == notification.UserId))
                    {
                        continue;
                    }
                    SendMessageToNotify(notification);
                }
                _logger.LogInformation("Successfully processed and saved batch of {Count} notifications", batch.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save batch to MongoDB");

                // Reject all messages and requeue them
                foreach (var tag in deliveryTags)
                {
                    _channel?.BasicNack(tag, false, true);
                }
            }

            _lastBatchProcess = DateTime.UtcNow;
        }
        finally
        {
            _batchSemaphore.Release();
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        // Process remaining messages before stopping
        if (!_messageQueue.IsEmpty)
        {
            _logger.LogInformation("Processing remaining messages before shutdown...");
            await ProcessBatchAsync(cancellationToken);
        }
        _channel?.Close();
        _batchSemaphore?.Dispose();

        await base.StopAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _batchSemaphore?.Dispose();

        base.Dispose();
    }

    // send to other service
    private void SendMessageToNotify(Notification notification)
    {
        _logger.LogInformation("Start sending notification.");
        var json = JsonSerializer.Serialize(notification);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = _channel?.CreateBasicProperties();
        if (properties == null)
        {
            return;
        }
        properties.Persistent = true;
        properties.ContentType = "application/json";

        _channel?.BasicPublish(exchange: _config.ExchangeName, routingKey: _config.RoutingKeySendNotify, basicProperties: properties, body: body);
        _logger.LogInformation("End sending notification.");
    }
}