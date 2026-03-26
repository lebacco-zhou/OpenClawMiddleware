using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenClawMiddleware.Models;

namespace OpenClawMiddleware.Handlers;

public interface IAuthHandler
{
    Task HandleAuthAsync(ConnectionContext context, EncryptedMessage message, CancellationToken ct);
}

public class AuthHandler : IAuthHandler
{
    private readonly ILogger<AuthHandler> _logger;
    private readonly IClientTokenService _tokenService;
    private readonly ICryptoService _cryptoService;

    public AuthHandler(
        ILogger<AuthHandler> logger,
        IClientTokenService tokenService,
        ICryptoService cryptoService)
    {
        _logger = logger;
        _tokenService = tokenService;
        _cryptoService = cryptoService;
    }

    public async Task HandleAuthAsync(ConnectionContext context, EncryptedMessage message, CancellationToken ct)
    {
        try
        {
            // 验证时间戳
            if (!_cryptoService.ValidateTimestamp(message.Timestamp))
            {
                await SendAuthResultAsync(context.Socket!, false, "Invalid timestamp");
                return;
            }

            // 验证 Nonce (防重放)
            var nonce = message.Nonce != null ? Convert.FromBase64String(message.Nonce) : Array.Empty<byte>();
            if (_cryptoService.IsNonceUsed(nonce))
            {
                await SendAuthResultAsync(context.Socket!, false, "Nonce already used");
                return;
            }

            // 验证客户端令牌
            if (string.IsNullOrEmpty(message.ClientId) || string.IsNullOrEmpty(message.Token))
            {
                await SendAuthResultAsync(context.Socket!, false, "Missing clientId or token");
                return;
            }

            var isValid = await _tokenService.ValidateTokenAsync(message.ClientId!, message.Token!);
            if (!isValid)
            {
                await SendAuthResultAsync(context.Socket!, false, "Invalid token");
                return;
            }

            // 标记 Nonce 已使用
            _cryptoService.MarkNonceAsUsed(nonce);

            // 生成会话密钥
            var sessionKey = RandomNumberGenerator.GetBytes(32); // 256-bit AES key
            
            // 用 RSA 公钥加密会话密钥返回给客户端
            var rsa = _cryptoService.GetRsaPrivateKey();
            var encryptedSessionKey = rsa.Encrypt(sessionKey, RSAEncryptionPadding.OaepSHA256);

            // 保存会话密钥到上下文
            context.ClientId = message.ClientId!;
            context.SessionKey = sessionKey;
            context.State = ConnectionState.Active;

            _logger.LogInformation("Client {ClientId} authenticated successfully", message.ClientId);

            await SendAuthResultAsync(context.Socket!, true, null, encryptedSessionKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during authentication");
            await SendAuthResultAsync(context.Socket!, false, "Authentication error");
        }
    }

    private async Task SendAuthResultAsync(WebSocket socket, bool success, string? errorMessage, byte[]? encryptedSessionKey = null)
    {
        var result = new
        {
            type = "auth_result",
            success = success,
            sessionId = success ? Guid.NewGuid().ToString() : null,
            encryptedSessionKey = encryptedSessionKey != null ? Convert.ToBase64String(encryptedSessionKey) : null,
            expiresIn = success ? 3600 : (int?)null,
            error = errorMessage
        };

        var json = JsonSerializer.Serialize(result);
        var bytes = Encoding.UTF8.GetBytes(json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
    }
}
