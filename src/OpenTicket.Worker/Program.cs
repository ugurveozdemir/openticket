using Microsoft.EntityFrameworkCore;
using OpenTicket.Infrastructure.Messaging;
using OpenTicket.Infrastructure.Persistence;
using OpenTicket.Infrastructure.Redis;
using OpenTicket.Worker;
using StackExchange.Redis;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

var redisConnection = await ConnectionMultiplexer.ConnectAsync(builder.Configuration["Redis:ConnectionString"]!);
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);
builder.Services.AddSingleton<IRedisSeatLockService, RedisSeatLockService>();

builder.Services.AddSingleton<IRabbitMqConnectionProvider>(_ => new RabbitMqConnectionProvider(
    builder.Configuration["RabbitMq:Host"]!,
    int.Parse(builder.Configuration["RabbitMq:Port"] ?? "5672"),
    builder.Configuration["RabbitMq:Username"] ?? "guest",
    builder.Configuration["RabbitMq:Password"] ?? "guest"));
builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

builder.Services.Configure<PaymentSimulationOptions>(
    builder.Configuration.GetSection(PaymentSimulationOptions.SectionName));

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<SeatHoldSweepService>();
builder.Services.AddHostedService<SeatExpiryNotificationService>();
builder.Services.AddHostedService<PaymentProcessingConsumer>();
builder.Services.AddHostedService<OrderFinalizationConsumer>();

var host = builder.Build();
host.Run();
