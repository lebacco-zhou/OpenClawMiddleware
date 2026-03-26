using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace OpenClawMiddleware.Services;

public interface IGatewayProxyService
{
    Task<string> ForwardAsync(string message, string senderId);
    Task<bool> HealthCheckAsync();
}

public class GatewayProxyService : IGatewayProxyService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GatewayProxyService> _logger;
    private readonly string _gatewayBaseUrl;
    private readonly string _gatewayToken;
    private readonly int _timeoutSeconds;
    private readonly int _retryCount;

    public GatewayProxyService(ILogger<GatewayProxyService> logger, IConfiguration config, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;
        _gatewayBaseUrl = config.GetValue<string>("Gateway:BaseUrl") ?? "http://localhost:18789";
        _gatewayToken = config.GetValue<string>("Gateway:Token") ?? "";
        _timeoutSeconds = config.GetValue<int>("Gateway:TimeoutSeconds", 30);
        _retryCount = config.GetValue<int>("Gateway:RetryCount", 3);
    }

    public async Task<string> ForwardAsync(string message, string senderId)
    {
        var request = new
        {
            channel = "middleware-client",
            senderId = senderId,
            message = message,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        var json = JsonSerializer.Serialize(request);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        for (int i = 0; i < _retryCount; i++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                
                // 使用 Gateway 的 API 端点
                var response = await _httpClient.PostAsync(
                    $"{_gatewayBaseUrl}/api/message",
                    content,
                    cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync();
                    _logger.LogDebug("Gateway response: {Response}", responseBody);
                    return responseBody;
                }

                _logger.LogWarning("Gateway returned {Status}: {Response}", 
                    response.StatusCode, await response.Content.ReadAsStringAsync());
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Gateway request timeout (attempt {Attempt}/{Max})", i + 1, _retryCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error forwarding to Gateway (attempt {Attempt}/{Max})", i + 1, _retryCount);
            }

            if (i < _retryCount - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i))); // 指数退避
            }
        }

        throw new Exception("Failed to forward message to Gateway after all retries");
    }

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            var response = await _httpClient.GetAsync($"{_gatewayBaseUrl}/api/status");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway health check failed");
            return false;
        }
    }
}
