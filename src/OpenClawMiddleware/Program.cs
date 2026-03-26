using Serilog;
using OpenClawMiddleware.Services;
using OpenClawMiddleware.Handlers;

var builder = WebApplication.CreateBuilder(args);

// 配置 Serilog 日志
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File("/var/log/openclaw/middleware.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// 注册服务
builder.Services.AddSingleton<IConnectionManager, ConnectionManager>();
builder.Services.AddSingleton<ICryptoService, CryptoService>();
builder.Services.AddSingleton<IGatewayProxyService, GatewayProxyService>();
builder.Services.AddSingleton<IMessageRouter, MessageRouter>();
builder.Services.AddSingleton<IWebSocketService, WebSocketService>();
builder.Services.AddSingleton<IFileStorageService, FileStorageService>();
builder.Services.AddSingleton<IClientTokenService, ClientTokenService>();
builder.Services.AddHostedService<HeartbeatService>();

var app = builder.Build();

// 配置中间件
app.UseMiddleware<AuthMiddleware>();
app.UseMiddleware<RateLimitMiddleware>();

// WebSocket 端点
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.Map("/ws", async (HttpContext httpContext, IWebSocketService wsService) =>
{
    if (httpContext.WebSockets.IsWebSocketRequest)
    {
        using var webSocket = await httpContext.WebSockets.AcceptWebSocketAsync();
        await wsService.HandleConnectionAsync(webSocket, httpContext.RequestAborted);
    }
    else
    {
        httpContext.Response.StatusCode = 400;
    }
});

// 健康检查
app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTimeOffset.UtcNow }));

// 启动 WebSocket 服务
var wsServiceInstance = app.Services.GetRequiredService<IWebSocketService>();
await wsServiceInstance.StartAsync(CancellationToken.None);

try
{
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
