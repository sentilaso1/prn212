using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WorkFlowPro.Services;

/// <summary>UC-08: periodically lock evaluations after the 24h dispute window.</summary>
public sealed class EvaluationFinalizationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EvaluationFinalizationHostedService> _logger;

    public EvaluationFinalizationHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<EvaluationFinalizationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var tasks = scope.ServiceProvider.GetRequiredService<ITaskService>();
                await tasks.FinalizeExpiredEvaluationsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Evaluation finalization sweep failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }
}
