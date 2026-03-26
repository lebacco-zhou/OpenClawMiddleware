using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenClawMiddleware.Models;
using OpenClawMiddleware.Handlers;

namespace OpenClawMiddleware.Services;

public interface IWebSocketService
{
    Task StartAsync(CancellationToken ct);
    Task HandleConnectionAsync(WebSocket webSocket, CancellationToken ct);
    int GetActiveConnectionCount();
}

public class WebSocketService : IWebSocketService
{
    private readonly ILogger<WebSocketService> _logger;
    private readonly IConnectionManager _connectionManager;
    private readonly IMessageRouter _messageRouter;
    private readonly IAuthHandler _authHandler;
    private readonly int _webSocketPort;

    public WebSocketService(
        ILogger<WebSocketService> logger,
        IConnectionManager connectionManager,
        IMessageRouter messageRouter,
        IAuthHandler authHandler,
        IConfiguration config)
    {
        _logger = logger;
        _connectionManager = connectionManager;
        _messageRouter = messageRouter;
        _authHandler = authHandler;
        _webSocketPort = config.GetValue<int>("Server:WebSocketPort", 8445);
    }

    public Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("WebSocket service started on port {Port}", _webSocketPort);
        return Task.CompletedTask;
    }

    public async Task HandleConnectionAsync(WebSocket webSocket, CancellationToken ct)
    {
        var buffer = new byte[1024 * 4];
        var context = new ConnectionContext
        {
            Socket = webSocket,
            ConnectedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow,
            State = ConnectionState.Handshaking
        };

        try
        {
            while (webSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var receiveResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);

                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", ct);
                    break;
                }

                var message = Encoding.UTF8.GetString(buffer, 0, receiveResult.Count);
                await ProcessMessageAsync(context, message, ct);

                context.LastActivity = DateTime.UtcNow;
            }
        }
        catch (WebSocketException ex) when (ex.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
        {
            _logger.LogDebug(ex, "Client connection closed prematurely");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing WebSocket message");
        }
        finally
        {
            if (!string.IsNullOrEmpty(context.ClientId))
            {
                _connectionManager.RemoveConnection(context.ClientId);
            }
            webSocket.Dispose();
        }
    }

    private async Task ProcessMessageAsync(ConnectionContext context, string message, CancellationToken ct)
    {
        try
        {
            var encryptedMessage = JsonSerializer.Deserialize<EncryptedMessage>(message);
            if (encryptedMessage == null)
            {
                return;
            }

            // 认证消息特殊处理
            if (encryptedMessage.Type == "auth")
            {
                await _authHandler.HandleAuthAsync(context, encryptedMessage, ct);
                return;
            }

            // 其他消息需要已认证
            if (context.State != ConnectionState.Active)
            {
                await SendErrorAsync(context.Socket!, "Not authenticated");
                return;
            }

            await _messageRouter.RouteAsync(context.ClientId, encryptedMessage, ct);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON message received");
            await SendErrorAsync(context.Socket!, "Invalid message format");
        }
    }

    private async Task SendErrorAsync(WebSocket socket, string errorMessage)
    {
        var error = new { type = "error", message = errorMessage };
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(error));
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }

    public int GetActiveConnectionCount() => _connectionManager.GetConnectionCount();
}
