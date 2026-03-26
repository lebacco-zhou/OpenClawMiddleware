using System.Text.Json.Serialization;

namespace OpenClawMiddleware.Models;

public class EncryptedMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
    
    [JsonPropertyName("messageId")]
    public string MessageId { get; set; } = string.Empty;
    
    [JsonPropertyName("timestamp")]
    public long Timestamp { get; set; }
    
    [JsonPropertyName("nonce")]
    public string? Nonce { get; set; }
    
    [JsonPropertyName("payload")]
    public string? Payload { get; set; }
    
    [JsonPropertyName("tag")]
    public string? Tag { get; set; }
    
    // 文件上传相关
    [JsonPropertyName("uploadId")]
    public string? UploadId { get; set; }
    
    [JsonPropertyName("chunkIndex")]
    public int? ChunkIndex { get; set; }
    
    [JsonPropertyName("totalChunks")]
    public int? TotalChunks { get; set; }
    
    [JsonPropertyName("data")]
    public string? Data { get; set; }
    
    // 认证相关
    [JsonPropertyName("clientId")]
    public string? ClientId { get; set; }
    
    [JsonPropertyName("token")]
    public string? Token { get; set; }
    
    [JsonPropertyName("deviceInfo")]
    public string? DeviceInfo { get; set; }
    
    [JsonPropertyName("success")]
    public bool? Success { get; set; }
    
    [JsonPropertyName("sessionId")]
    public string? SessionId { get; set; }
    
    [JsonPropertyName("encryptedSessionKey")]
    public string? EncryptedSessionKey { get; set; }
    
    [JsonPropertyName("expiresIn")]
    public int? ExpiresIn { get; set; }
}
