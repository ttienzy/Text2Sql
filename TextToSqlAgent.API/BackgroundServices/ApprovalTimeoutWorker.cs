using TextToSqlAgent.Core.Interfaces;

namespace TextToSqlAgent.API.BackgroundServices;

/// <summary>
/// Background worker that periodically checks for expired approval requests
/// and marks them as timed out
/// </summary>
public class ApprovalTimeoutWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApprovalTimeoutWorker> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(1);

    public ApprovalTimeoutWorker(
        IServiceProvider serviceProvider,
        ILogger<ApprovalTimeoutWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[ApprovalTimeoutWorker] Starting background worker");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, stoppingToken);

                using var scope = _serviceProvider.CreateScope();
                var approvalService = scope.ServiceProvider.GetRequiredService<IApprovalQueueService>();

                var timedOutCount = await approvalService.TimeoutExpiredApprovalsAsync(stoppingToken);

                if (timedOutCount > 0)
                {
                    _logger.LogInformation(
                        "[ApprovalTimeoutWorker] Marked {Count} approvals as timed out",
                        timedOutCount);
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when stopping
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ApprovalTimeoutWorker] Error checking for expired approvals");
            }
        }

        _logger.LogInformation("[ApprovalTimeoutWorker] Stopped");
    }
}
