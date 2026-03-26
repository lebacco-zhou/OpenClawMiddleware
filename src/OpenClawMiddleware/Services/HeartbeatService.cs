namespace OpenClawMiddleware.Services;

public interface IHeartbeatService
{
    Task StartAsync(CancellationToken ct);
}

public class HeartbeatService : BackgroundService, IHeartbeatService
{
    private readonly ILogger<HeartbeatService> _logger;
    private readonly IConnectionManager _connectionManager;
    private readonly IGatewayProxyService _gatewayProxy;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _connectionTimeout;

    public HeartbeatService(
        ILogger<HeartbeatService> logger,
        IConnectionManager connectionManager,
        IGatewayProxyService gatewayProxy,
        IConfiguration config)
    {
        _logger = logger;
        _connectionManager = connectionManager;
        _gatewayProxy = gatewayProxy;
        _heartbeatInterval = TimeSpan.FromSeconds(config.GetValue<int>("Heartbeat:IntervalSeconds", 30));
        _connectionTimeout = TimeSpan.FromSeconds(config.GetValue<int>("Heartbeat:TimeoutSeconds", 90));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Heartbeat service started with interval {Interval}s", _heartbeatInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                CheckClientHeartbeats();
                await CheckGatewayHealthAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in heartbeat check");
            }

            await Task.Delay(_heartbeatInterval, stoppingToken);
        }
    }

    private void CheckClientHeartbeats()
    {
        _connectionManager.CleanupIdleConnections(_connectionTimeout);
    }

    private async Task CheckGatewayHealthAsync()
    {
        var isHealthy = await _gatewayProxy.HealthCheckAsync();
        if (!isHealthy)
        {
            _logger.LogWarning("Gateway health check failed");
        }
    }

    public new Task StartAsync(CancellationToken ct)
    {
        return Task.CompletedTask;
    }
}
