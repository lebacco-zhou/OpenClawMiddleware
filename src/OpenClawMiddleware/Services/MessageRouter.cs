using System.Text.Json;
using OpenClawMiddleware.Models;

namespace OpenClawMiddleware.Services;

public interface IMessageRouter
{
    Task RouteAsync(string clientId, EncryptedMessage message, CancellationToken ct);
}

public class MessageRouter : IMessageRouter
{
    private readonly ILogger<MessageRouter> _logger;
    private readonly IConnectionManager _connectionManager;
    private readonly IGatewayProxyService _gatewayProxy;
    private readonly ICryptoService _cryptoService;

    public MessageRouter(
        ILogger<MessageRouter> logger,
        IConnectionManager connectionManager,
        IGatewayProxyService gatewayProxy,
        ICryptoService cryptoService)
    {
        _logger = logger;
        _connectionManager = connectionManager;
        _gatewayProxy = gatewayProxy;
        _cryptoService = cryptoService;
    }

    public async Task RouteAsync(string clientId, EncryptedMessage message, CancellationToken ct)
    {
        try
        {
            switch (message.Type)
            {
                case "chat":
                    await HandleChatMessageAsync(clientId, message, ct);
                    break;

                case "heartbeat":
                    await HandleHeartbeatAsync(clientId, message, ct);
                    break;

                case "file_upload_request":
                    await HandleFileUploadRequestAsync(clientId, message, ct);
                    break;

                case "file_chunk":
                    await HandleFileChunkAsync(clientId, message, ct);
                    break;

                case "file_upload_complete":
                    await HandleFileUploadCompleteAsync(clientId, message, ct);
                    break;

                default:
                    _logger.LogWarning("Unknown message type: {Type}", message.Type);
                    await SendErrorAsync(clientId, $"Unknown message type: {message.Type}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error routing message type {Type}", message.Type);
            await SendErrorAsync(clientId, $"Internal error: {ex.Message}");
        }
    }

    private async Task HandleChatMessageAsync(string clientId, EncryptedMessage message, CancellationToken ct)
    {
        // 解密消息
        var decryptedMessage = await DecryptMessageAsync(message);
        
        // 转发到 Gateway
        var response = await _gatewayProxy.ForwardAsync(decryptedMessage, clientId);
        
        // 发送响应回客户端
        await _connectionManager.SendToClientAsync(clientId, response);
    }

    private async Task HandleHeartbeatAsync(string clientId, EncryptedMessage message, CancellationToken ct)
    {
        var context = _connectionManager.GetConnection(clientId);
        if (context != null)
        {
            context.LastActivity = DateTime.UtcNow;
        }

        var ack = new EncryptedMessage
        {
            Type = "heartbeat_ack",
            MessageId = message.MessageId,
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        await _connectionManager.SendToClientAsync(clientId, JsonSerializer.Serialize(ack));
    }

    private async Task HandleFileUploadRequestAsync(string clientId, EncryptedMessage message, CancellationToken ct)
    {
        // TODO: 实现文件上传请求处理
        await SendErrorAsync(clientId, "File upload not yet implemented");
    }

    private async Task HandleFileChunkAsync(string clientId, EncryptedMessage message, CancellationToken ct)
    {
        // TODO: 实现文件分块处理
        await Task.CompletedTask;
    }

    private async Task HandleFileUploadCompleteAsync(string clientId, EncryptedMessage message, CancellationToken ct)
    {
        // TODO: 实现文件上传完成处理
        await Task.CompletedTask;
    }

    private async Task<string> DecryptMessageAsync(EncryptedMessage message)
    {
        // TODO: 实现解密逻辑
        // 需要获取会话密钥并解密
        return message.Payload ?? "";
    }

    private async Task SendErrorAsync(string clientId, string errorMessage)
    {
        var error = new EncryptedMessage
        {
            Type = "error",
            MessageId = Guid.NewGuid().ToString(),
            Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            Payload = errorMessage
        };

        await _connectionManager.SendToClientAsync(clientId, JsonSerializer.Serialize(error));
    }
}
