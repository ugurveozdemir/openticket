using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTicket.Api.Endpoints;
using OpenTicket.Infrastructure.Auth;
using OpenTicket.Infrastructure.Messaging;
using OpenTicket.Infrastructure.Persistence;
using OpenTicket.Infrastructure.Redis;
using RabbitMQ.Client;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, config) => config
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console());

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Bearer {token}\""
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        { new OpenApiSecuritySchemeReference("Bearer", document, null), new List<string>() }
    });
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });

builder.Services.AddAuthorization();

var redisConnection = await ConnectionMultiplexer.ConnectAsync(builder.Configuration["Redis:ConnectionString"]!);
builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);
builder.Services.AddSingleton<IRedisSeatLockService, RedisSeatLockService>();

builder.Services.AddSingleton<IRabbitMqConnectionProvider>(_ => new RabbitMqConnectionProvider(
    builder.Configuration["RabbitMq:Host"]!,
    int.Parse(builder.Configuration["RabbitMq:Port"] ?? "5672"),
    builder.Configuration["RabbitMq:Username"] ?? "guest",
    builder.Configuration["RabbitMq:Password"] ?? "guest"));
builder.Services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();

builder.Services.AddHealthChecks()
    .AddNpgSql(
        connectionString: builder.Configuration.GetConnectionString("Postgres")!,
        name: "postgres")
    .AddRedis(
        redisConnectionString: builder.Configuration["Redis:ConnectionString"]!,
        name: "redis")
    .AddRabbitMQ(
        _ =>
        {
            var factory = new ConnectionFactory
            {
                HostName = builder.Configuration["RabbitMq:Host"]!,
                Port = int.Parse(builder.Configuration["RabbitMq:Port"] ?? "5672"),
                UserName = builder.Configuration["RabbitMq:Username"] ?? "guest",
                Password = builder.Configuration["RabbitMq:Password"] ?? "guest"
            };
            return factory.CreateConnectionAsync();
        },
        name: "rabbitmq");

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
}

{
    var connectionProvider = app.Services.GetRequiredService<IRabbitMqConnectionProvider>();
    var connection = await connectionProvider.GetConnectionAsync();
    await using var channel = await connection.CreateChannelAsync();
    await Topology.DeclareAllAsync(channel);
}

app.UseSwagger();
app.UseSwaggerUI();

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = WriteHealthReportAsync
});

app.MapAuthEndpoints();
app.MapVenueEndpoints();
app.MapEventEndpoints();
app.MapSeatHoldEndpoints();
app.MapOrderEndpoints();

app.Run();

static Task WriteHealthReportAsync(HttpContext context, HealthReport report)
{
    context.Response.ContentType = "application/json";
    var payload = JsonSerializer.Serialize(new
    {
        status = report.Status.ToString(),
        checks = report.Entries.Select(e => new
        {
            name = e.Key,
            status = e.Value.Status.ToString(),
            error = e.Value.Exception?.Message
        }),
        durationMs = report.TotalDuration.TotalMilliseconds
    });
    return context.Response.WriteAsync(payload);
}
