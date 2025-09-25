using NotifyService.Api.Hubs;
using NotifyService.Application.Services;
using NotifyService.Configurations;
using NotifyService.Converters;
using NotifyService.Domain.Interfaces;
using NotifyService.Infrastructure.Data;
using NotifyService.Infrastructure.Data.Repositories;
using NotifyService.Infrastructure.Messaging;
using NotifyService.Infrastructure.Workers;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services.Configure<RabbitMQSetting>(builder.Configuration.GetSection("RabbitMQ"));

// Add CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        // policy.WithOrigins("http://127.0.0.1:5500");
        policy
              .AllowAnyMethod()
              .AllowAnyHeader()
              .SetIsOriginAllowed(_ => true)
              .AllowCredentials();
    });
});

// SignalR with Redis backplane
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
})
.AddStackExchangeRedis(builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379");;

// Add health checks
builder.Services.AddHealthChecks()
    .AddMongoDb(
        builder.Configuration.GetConnectionString("MongoDB") ?? "mongodb://localhost:27017",
        name: "mongodb",
        timeout: TimeSpan.FromSeconds(3))
    .AddRabbitMQ(
        rabbitConnectionString: $"amqp://{builder.Configuration["RabbitMQ:UserName"]}:{builder.Configuration["RabbitMQ:Password"]}@{builder.Configuration["RabbitMQ:HostName"]}:{builder.Configuration["RabbitMQ:Port"]}/",
        name: "rabbitmq",
        timeout: TimeSpan.FromSeconds(3));
        
builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
{
    var connectionString = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
    return ConnectionMultiplexer.Connect(connectionString);
});

builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
builder.Services.AddSingleton<IMessagePublisher, RabbitMQPublisher>();
builder.Services.AddSingleton<INotificationRepository, NotificationRepository>();
builder.Services.AddSingleton<IRedisConnectionMappingService, RedisConnectionMappingService>();
builder.Services.AddScoped<ISignalRNotificationService, SignalRNotificationService>();

// Add hosted service
builder.Services.AddHostedService<MessageConsumerWorker>();
builder.Services.AddHostedService<NotifyConsumerWorker>();
// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new DictionaryObjectJsonConverter());
    }); ;
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.UseCors("AllowAll");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");
app.MapHub<SendNotificationHub>("/hub/notifyHub").RequireCors("AllowAll");

app.Run();