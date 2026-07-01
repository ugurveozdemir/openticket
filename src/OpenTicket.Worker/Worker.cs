using Npgsql;
using RabbitMQ.Client;
using StackExchange.Redis;

namespace OpenTicket.Worker;

public class Worker(ILogger<Worker> logger, IConfiguration configuration) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await CheckPostgresAsync(stoppingToken);
        await CheckRedisAsync(stoppingToken);
        await CheckRabbitMqAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Worker alive at: {Time}", DateTimeOffset.UtcNow);
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }

    private async Task CheckPostgresAsync(CancellationToken ct)
    {
        try
        {
            await using var connection = new NpgsqlConnection(configuration.GetConnectionString("Postgres"));
            await connection.OpenAsync(ct);
            logger.LogInformation("Postgres connection OK");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Postgres connection FAILED");
        }
    }

    private async Task CheckRedisAsync(CancellationToken ct)
    {
        try
        {
            await using var redis = await ConnectionMultiplexer.ConnectAsync(configuration["Redis:ConnectionString"]!);
            await redis.GetDatabase().PingAsync();
            logger.LogInformation("Redis connection OK");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Redis connection FAILED");
        }
    }

    private async Task CheckRabbitMqAsync(CancellationToken ct)
    {
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = configuration["RabbitMq:Host"]!,
                Port = int.Parse(configuration["RabbitMq:Port"] ?? "5672"),
                UserName = configuration["RabbitMq:Username"] ?? "guest",
                Password = configuration["RabbitMq:Password"] ?? "guest"
            };
            await using var connection = await factory.CreateConnectionAsync(ct);
            logger.LogInformation("RabbitMQ connection OK");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "RabbitMQ connection FAILED");
        }
    }
}
