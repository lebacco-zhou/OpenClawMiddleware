using System.Collections.Concurrent;

namespace OpenClawMiddleware.Middleware;

public class RateLimitMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitMiddleware> _logger;
    private readonly ConcurrentDictionary<string, RateLimitInfo> _clientLimits = new();
    private readonly int _maxMessagesPerMinute;
    private readonly int _burstLimit;

    public RateLimitMiddleware(RequestDelegate next, ILogger<RateLimitMiddleware> logger, IConfiguration config)
    {
        _next = next;
        _logger = logger;
        _maxMessagesPerMinute = config.GetValue<int>("RateLimit:MaxMessagesPerMinute", 100);
        _burstLimit = config.GetValue<int>("RateLimit:BurstLimit", 20);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var clientId = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        if (!CheckRateLimit(clientId))
        {
            context.Response.StatusCode = 429;
            await context.Response.WriteAsJsonAsync(new { error = "Rate limit exceeded" });
            return;
        }

        await _next(context);
    }

    private bool CheckRateLimit(string clientId)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddMinutes(-1);

        var info = _clientLimits.GetOrAdd(clientId, _ => new RateLimitInfo());

        lock (info)
        {
            // 清理旧记录
            info.MessageTimestamps = info.MessageTimestamps.Where(t => t > windowStart).ToList();

            // 检查速率限制
            if (info.MessageTimestamps.Count >= _maxMessagesPerMinute)
            {
                _logger.LogWarning("Rate limit exceeded for client {ClientId}", clientId);
                return false;
            }

            // 检查突发限制
            var recentCount = info.MessageTimestamps.Count(t => now - t < TimeSpan.FromSeconds(1));
            if (recentCount >= _burstLimit)
            {
                _logger.LogWarning("Burst limit exceeded for client {ClientId}", clientId);
                return false;
            }

            info.MessageTimestamps.Add(now);
            return true;
        }
    }

    private class RateLimitInfo
    {
        public List<DateTime> MessageTimestamps { get; set; } = new();
    }
}
