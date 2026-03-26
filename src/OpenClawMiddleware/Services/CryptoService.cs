using System.Security.Cryptography;
using System.Collections.Concurrent;

namespace OpenClawMiddleware.Services;

public interface ICryptoService
{
    Task<byte[]> NegotiateSessionKeyAsync(byte[] encryptedKey);
    Task<(byte[] payload, byte[] tag)> DecryptAsync(byte[] ciphertext, byte[] nonce, byte[] tag);
    Task<(byte[] payload, byte[] tag)> EncryptAsync(byte[] plaintext, byte[] nonce, byte[] sessionKey);
    bool ValidateTimestamp(long timestamp);
    bool IsNonceUsed(byte[] nonce);
    void MarkNonceAsUsed(byte[] nonce);
    RSA GetRsaPrivateKey();
}

public class CryptoService : ICryptoService, IDisposable
{
    private readonly ILogger<CryptoService> _logger;
    private readonly ConcurrentDictionary<string, DateTime> _usedNonces = new();
    private readonly RSA _rsa;
    private readonly int _maxNonceAgeSeconds;

    public CryptoService(ILogger<CryptoService> logger, IConfiguration config)
    {
        _logger = logger;
        _maxNonceAgeSeconds = config.GetValue<int>("Security:MaxNonceAgeSeconds", 300);
        
        var keyPath = config.GetValue<string>("Security:RsaKeyPath");
        _rsa = LoadOrCreateRsaKey(keyPath);
    }

    private RSA LoadOrCreateRsaKey(string? keyPath)
    {
        if (!string.IsNullOrEmpty(keyPath) && File.Exists(keyPath))
        {
            var rsa = RSA.Create();
            rsa.ImportFromPem(File.ReadAllText(keyPath));
            _logger.LogInformation("Loaded RSA key from {Path}", keyPath);
            return rsa;
        }

        var newRsa = RSA.Create(2048);
        if (!string.IsNullOrEmpty(keyPath))
        {
            var directory = Path.GetDirectoryName(keyPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(keyPath, newRsa.ExportToPem());
            _logger.LogInformation("Created new RSA key at {Path}", keyPath);
        }
        return newRsa;
    }

    public Task<byte[]> NegotiateSessionKeyAsync(byte[] encryptedKey)
    {
        var sessionKey = _rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);
        return Task.FromResult(sessionKey);
    }

    public Task<(byte[] payload, byte[] tag)> DecryptAsync(byte[] ciphertext, byte[] nonce, byte[] tag)
    {
        using var aes = new AesGcm(32); // Will be set with actual key
        var plaintext = new byte[ciphertext.Length];
        aes.Decrypt(nonce, ciphertext, tag, plaintext);
        return Task.FromResult((plaintext, tag));
    }

    public Task<(byte[] payload, byte[] tag)> EncryptAsync(byte[] plaintext, byte[] nonce, byte[] sessionKey)
    {
        using var aes = new AesGcm(sessionKey);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[16];
        aes.Encrypt(nonce, plaintext, ciphertext, tag);
        return Task.FromResult((ciphertext, tag));
    }

    public bool ValidateTimestamp(long timestamp)
    {
        var messageTime = DateTimeOffset.FromUnixTimeMilliseconds(timestamp).UtcDateTime;
        var age = DateTime.UtcNow - messageTime;
        return age.TotalSeconds <= _maxNonceAgeSeconds && age.TotalSeconds >= -60; // Allow 1 min clock skew
    }

    public bool IsNonceUsed(byte[] nonce)
    {
        var nonceKey = Convert.ToBase64String(nonce);
        return _usedNonces.ContainsKey(nonceKey);
    }

    public void MarkNonceAsUsed(byte[] nonce)
    {
        var nonceKey = Convert.ToBase64String(nonce);
        _usedNonces[nonceKey] = DateTime.UtcNow;
        CleanupOldNonces();
    }

    private void CleanupOldNonces()
    {
        var cutoff = DateTime.UtcNow.AddSeconds(-_maxNonceAgeSeconds);
        var oldKeys = _usedNonces.Where(kvp => kvp.Value < cutoff).Select(kvp => kvp.Key).ToList();
        foreach (var key in oldKeys)
        {
            _usedNonces.TryRemove(key, out _);
        }
    }

    public RSA GetRsaPrivateKey() => _rsa;

    public void Dispose()
    {
        _rsa.Dispose();
    }
}
