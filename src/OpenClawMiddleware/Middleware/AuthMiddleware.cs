namespace OpenClawMiddleware.Middleware;

public class AuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<AuthMiddleware> _logger;

    public AuthMiddleware(RequestDelegate next, ILogger<AuthMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // WebSocket 认证在连接建立后通过消息处理
        // 这里可以添加 HTTP 请求的认证逻辑
        await _next(context);
    }
}
