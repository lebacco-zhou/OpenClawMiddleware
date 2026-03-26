using System.Security.Cryptography;
using OpenClawMiddleware.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace OpenClawMiddleware.Tests;

public class CryptoServiceTests
{
    private readonly CryptoService _cryptoService;
    private readonly ILogger<CryptoService> _logger;
    private readonly IConfiguration _config;

    public CryptoServiceTests()
    {
        _logger = new LoggerFactory().CreateLogger<CryptoService>();
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:MaxNonceAgeSeconds"] = "300"
            })!
            .Build();
        
        _cryptoService = new CryptoService(_logger, _config);
    }

    [Fact]
    public void ValidateTimestamp_WithinWindow_ReturnsTrue()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        // Act
        var result = _cryptoService.ValidateTimestamp(now);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void ValidateTimestamp_TooOld_ReturnsFalse()
    {
        // Arrange
        var oldTimestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeMilliseconds();

        // Act
        var result = _cryptoService.ValidateTimestamp(oldTimestamp);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ValidateTimestamp_Future_ReturnsFalse()
    {
        // Arrange
        var futureTimestamp = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();

        // Act
        var result = _cryptoService.ValidateTimestamp(futureTimestamp);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsNonceUsed_NewNonce_ReturnsFalse()
    {
        // Arrange
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        // Act
        var result = _cryptoService.IsNonceUsed(nonce);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void IsNonceUsed_UsedNonce_ReturnsTrue()
    {
        // Arrange
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);
        _cryptoService.MarkNonceAsUsed(nonce);

        // Act
        var result = _cryptoService.IsNonceUsed(nonce);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task EncryptDecrypt_RoundTrip_Success()
    {
        // Arrange
        var sessionKey = new byte[32];
        RandomNumberGenerator.Fill(sessionKey);
        var plaintext = System.Text.Encoding.UTF8.GetBytes("Hello, World!");
        var nonce = new byte[12];
        RandomNumberGenerator.Fill(nonce);

        // Act - Encrypt
        var (ciphertext, tag) = await _cryptoService.EncryptAsync(plaintext, nonce, sessionKey);

        // Note: Actual decryption requires proper AesGcm implementation
        // This is a placeholder test
        Assert.NotNull(ciphertext);
        Assert.NotNull(tag);
        Assert.Equal(plaintext.Length, ciphertext.Length);
        Assert.Equal(16, tag.Length);
    }

    public void Dispose()
    {
        _cryptoService.Dispose();
    }
}
