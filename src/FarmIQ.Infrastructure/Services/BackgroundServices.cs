using System.Threading.Channels;
using FarmIQ.Application.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using FarmIQ.Infrastructure.Configuration;

namespace FarmIQ.Infrastructure.Services;

public sealed class InMemoryBackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>();

    public ValueTask QueueAsync(Guid jobId, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(jobId, cancellationToken);

    public async ValueTask WaitForSignalAsync(CancellationToken cancellationToken)
    {
        if (_channel.Reader.TryRead(out _))
        {
            return;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(3));

        try
        {
            await _channel.Reader.ReadAsync(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
        }
    }
}

public sealed class AdvisoryProcessingWorker(
    IServiceScopeFactory serviceScopeFactory,
    IBackgroundJobQueue backgroundJobQueue,
    IOptions<ProcessingOptions> processingOptions,
    ILogger<AdvisoryProcessingWorker> logger) : BackgroundService
{
    private readonly string _workerId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await backgroundJobQueue.WaitForSignalAsync(stoppingToken);
                using var scope = serviceScopeFactory.CreateScope();
                var leaseService = scope.ServiceProvider.GetRequiredService<IProcessingJobLeaseService>();
                var workflow = scope.ServiceProvider.GetRequiredService<IAdvisoryWorkflowService>();

                var claimedJob = await leaseService.ClaimNextAsync(_workerId, stoppingToken);
                if (claimedJob is null)
                {
                    await Task.Delay(TimeSpan.FromSeconds(processingOptions.Value.PollIntervalSeconds), stoppingToken);
                    continue;
                }

                await workflow.ProcessAsync(claimedJob.Id, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Advisory processing worker failed.");
            }
        }
    }
}
