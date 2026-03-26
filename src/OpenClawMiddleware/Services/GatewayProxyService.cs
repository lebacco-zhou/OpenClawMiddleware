using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace OpenClawMiddleware.Services;

public interface IGatewayProxyService
{
    Task<string> ForwardAsync(string message, string senderId);
    Task<bool> HealthCheckAsync();
    Task InitializeAsync();
}

public class GatewayProxyService : IGatewayProxyService
{
    private ClientWebSocket? _webSocket;
    private readonly ILogger<GatewayProxyService> _logger;
    private string _gatewayWsUrl;
    private string _gatewayToken;
    private int _timeoutSeconds;
    private int _retryCount;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingRequests = new();
    private bool _isConnected = false;
    private CancellationTokenSource? _heartbeatCts;

    public GatewayProxyService(ILogger<GatewayProxyService> logger, IConfiguration config)
    {
        _logger = logger;
        var gatewayBaseUrl = config.GetValue<string>("Gateway:BaseUrl") ?? "http://localhost:18789";
        // 将 HTTP URL 转換為 WebSocket URL
        _gatewayWsUrl = gatewayBaseUrl.Replace("https://", "wss://").Replace("http://", "ws://");
        _gatewayToken = config.GetValue<string>("Gateway:Token") ?? "";
        _timeoutSeconds = config.GetValue<int>("Gateway:TimeoutSeconds", 30);
        _retryCount = config.GetValue<int>("Gateway:RetryCount", 3);
    }

    public async Task InitializeAsync()
    {
        _ = Task.Run(WebSocketReceiveLoopAsync); // 启动接收循环
    }

    public async Task<string> ForwardAsync(string message, string senderId)
    {
        var messageId = Guid.NewGuid().ToString();
        
        var request = new
        {
            channel = "middleware-client",
            senderId = senderId,
            message = message,
            timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            messageId = messageId
        };

        var json = JsonSerializer.Serialize(request);
        var bytes = Encoding.UTF8.GetBytes(json);

        var tcs = new TaskCompletionSource<string>();
        _pendingRequests[messageId] = tcs;

        for (int i = 0; i < _retryCount; i++)
        {
            try
            {
                await EnsureConnectedAsync();

                if (_webSocket?.State != WebSocketState.Open)
                {
                    throw new Exception("WebSocket is not connected");
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
                
                await _webSocket.SendAsync(
                    new ArraySegment<byte>(bytes),
                    WebSocketMessageType.Text,
                    true,
                    cts.Token);

                // 等待响应
                var response = await tcs.Task.TimeoutAfter(TimeSpan.FromSeconds(_timeoutSeconds));
                return response;
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Gateway request timeout (attempt {Attempt}/{Max})", i + 1, _retryCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error forwarding to Gateway (attempt {Attempt}/{Max})", i + 1, _retryCount);
                _isConnected = false; // 标记为断开连接
            }

            if (i < _retryCount - 1)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, i))); // 指数退避
            }
        }

        _pendingRequests.TryRemove(messageId, out _);
        throw new Exception("Failed to forward message to Gateway after all retries");
    }

    public async Task<bool> HealthCheckAsync()
    {
        try
        {
            await EnsureConnectedAsync();
            return _isConnected && _webSocket?.State == WebSocketState.Open;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gateway health check failed");
            _isConnected = false;
            return false;
        }
    }

    private async Task EnsureConnectedAsync()
    {
        if (_isConnected && _webSocket?.State == WebSocketState.Open)
        {
            return;
        }

        await _lock.WaitAsync();
        try
        {
            if (_isConnected && _webSocket?.State == WebSocketState.Open)
            {
                return; // 双重检查
            }

            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();
            
            // 设置授权头 - 使用 Gateway Token 直接认证
            _webSocket.Options.SetRequestHeader("Authorization", $"Bearer {_gatewayToken}");
            
            await _webSocket.ConnectAsync(new Uri($"{_gatewayWsUrl}/ws"), CancellationToken.None);
            _isConnected = true;
            _logger.LogInformation("Connected to Gateway at {GatewayWsUrl}", _gatewayWsUrl);
            
            // 启动心跳机制
            StartHeartbeat();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to Gateway at {GatewayWsUrl}", _gatewayWsUrl);
            _isConnected = false;
            throw;
        }
        finally
        {
            _lock.Release();
        }
    }
    
    private void StartHeartbeat()
    {
        _heartbeatCts?.Cancel();
        _heartbeatCts = new CancellationTokenSource();
        
        _ = Task.Run(async () =>
        {
            var heartbeatInterval = TimeSpan.FromSeconds(15); // 每15秒发送一次心跳，小于20秒超时
            
            while (!_heartbeatCts.Token.IsCancellationRequested)
            {
                try
                {
                    if (_isConnected && _webSocket?.State == WebSocketState.Open)
                    {
                        // 发送心跳消息
                        var heartbeatMessage = new
                        {
                            type = "event",
                            @event = "connect.heartbeat",
                            payload = new
                            {
                                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                            }
                        };
                        
                        var json = JsonSerializer.Serialize(heartbeatMessage);
                        var bytes = Encoding.UTF8.GetBytes(json);
                        
                        await _webSocket.SendAsync(
                            new ArraySegment<byte>(bytes),
                            WebSocketMessageType.Text,
                            true,
                            _heartbeatCts.Token);
                            
                        _logger.LogDebug("Sent heartbeat to Gateway");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error sending heartbeat to Gateway");
                }
                
                await Task.Delay(heartbeatInterval, _heartbeatCts.Token);
            }
        }, _heartbeatCts.Token);
    }
    
    private async Task WebSocketReceiveLoopAsync()
    {
        var buffer = new byte[4096];
        
        while (!CancellationToken.None.IsCancellationRequested)
        {
            try
            {
                if (_webSocket?.State != WebSocketState.Open)
                {
                    await Task.Delay(1000); // 等待重连
                    continue;
                }

                var result = await _webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogInformation("Gateway WebSocket closed, attempting to reconnect...");
                    _isConnected = false;
                    await Task.Delay(1000);
                    continue;
                }

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    _logger.LogDebug("Received from Gateway: {Json}", json);
                    
                    try
                    {
                        using var doc = JsonDocument.Parse(json);
                        
                        // 检查是否有 messageId
                        if (doc.RootElement.TryGetProperty("messageId", out var messageIdElement))
                        {
                            var messageId = messageIdElement.GetString();
                            if (!string.IsNullOrEmpty(messageId) && _pendingRequests.TryRemove(messageId, out var tcs))
                            {
                                _logger.LogDebug("Sending response for request {MessageId}", messageId);
                                tcs.TrySetResult(json);
                            }
                            else
                            {
                                // 这是一个不属于任何请求的响应，可能是广播消息
                                _logger.LogDebug("Received unsolicited message from Gateway: {Json}", json);
                            }
                        }
                        else
                        {
                            // 消息没有 messageId，可能是直接响应或广播
                            _logger.LogDebug("Received message without messageId: {Json}", json);
                        }
                    }
                    catch (JsonException jex)
                    {
                        _logger.LogError(jex, "Invalid JSON received from Gateway: {Json}", json);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing Gateway response: {Json}", json);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Gateway WebSocket receive loop");
                _isConnected = false;
                
                // 等待一段时间后重试
                await Task.Delay(1000);
            }
        }
    }
}

public static class TaskExtensions
{
    public static async Task<T> TimeoutAfter<T>(this Task<T> task, TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var delayTask = Task.Delay(timeout, cts.Token);
        var resultTask = await Task.WhenAny(task, delayTask);
        
        if (resultTask == delayTask)
        {
            throw new TimeoutException($"Task timed out after {timeout}");
        }
        
        cts.Cancel(); // 取消延迟任务
        return await task;
    }
}