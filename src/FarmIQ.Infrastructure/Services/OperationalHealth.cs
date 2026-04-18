using FarmIQ.Infrastructure.Configuration;
using FarmIQ.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace FarmIQ.Infrastructure.Services;

public sealed class WorkerHeartbeat
{
    private long _lastHeartbeatTicks = DateTime.UtcNow.Ticks;

    public DateTime LastHeartbeatUtc => new(Interlocked.Read(ref _lastHeartbeatTicks), DateTimeKind.Utc);

    public void RecordHeartbeat() =>
        Interlocked.Exchange(ref _lastHeartbeatTicks, DateTime.UtcNow.Ticks);
}

public sealed class DatabaseHealthCheck(FarmIQDbContext dbContext) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await dbContext.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy("Database connection is healthy.")
            : HealthCheckResult.Unhealthy("Database connection failed.");
    }
}

public sealed class WorkerHeartbeatHealthCheck(
    WorkerHeartbeat heartbeat,
    IOptions<ProcessingOptions> processingOptions) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var lastHeartbeatUtc = heartbeat.LastHeartbeatUtc;
        var staleThreshold = TimeSpan.FromSeconds(Math.Max(processingOptions.Value.PollIntervalSeconds * 2, 60));

        if (DateTime.UtcNow - lastHeartbeatUtc <= staleThreshold)
        {
            return Task.FromResult(HealthCheckResult.Healthy("Worker heartbeat is current."));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy($"Worker heartbeat is stale. Last heartbeat: {lastHeartbeatUtc:u}."));
    }
}
