using System.Text.Json;

namespace OpenClawMiddleware.Services;

public interface IClientTokenService
{
    Task<bool> ValidateTokenAsync(string clientId, string token);
    Task<string> CreateClientAsync(string name);
    Task<bool> DisableClientAsync(string clientId);
    Task<bool> DeleteClientAsync(string clientId);
    Task<IEnumerable<ClientTokenInfo>> ListClientsAsync();
}

public class ClientTokenInfo
{
    public string ClientId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool Enabled { get; set; } = true;
}

public class ClientTokenService : IClientTokenService
{
    private readonly string _tokenFilePath;
    private readonly ILogger<ClientTokenService> _logger;
    private readonly bool _allowDynamicRegistration;
    private List<ClientTokenInfo> _clients = new();

    public ClientTokenService(ILogger<ClientTokenService> logger, IConfiguration config)
    {
        _logger = logger;
        _tokenFilePath = config.GetValue<string>("ClientTokens:TokenFilePath") 
            ?? "/etc/openclaw/middleware/client-tokens.json";
        _allowDynamicRegistration = config.GetValue<bool>("ClientTokens:AllowDynamicRegistration", false);
        
        LoadTokens();
    }

    private void LoadTokens()
    {
        if (File.Exists(_tokenFilePath))
        {
            var json = File.ReadAllText(_tokenFilePath);
            var data = JsonSerializer.Deserialize<TokenFileData>(json);
            _clients = data?.Clients ?? new List<ClientTokenInfo>();
            _logger.LogInformation("Loaded {Count} client tokens from {Path}", _clients.Count, _tokenFilePath);
        }
        else
        {
            _clients = new List<ClientTokenInfo>();
            _logger.LogWarning("Token file not found: {Path}", _tokenFilePath);
        }
    }

    private void SaveTokens()
    {
        var directory = Path.GetDirectoryName(_tokenFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var data = new TokenFileData { Clients = _clients };
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_tokenFilePath, json);
    }

    public Task<bool> ValidateTokenAsync(string clientId, string token)
    {
        var client = _clients.FirstOrDefault(c => c.ClientId == clientId && c.Enabled);
        if (client == null)
        {
            return Task.FromResult(false);
        }

        // Token 可以是明文或加密形式
        var isValid = client.Token == token || 
                      (client.Token.StartsWith("mt_") && client.Token.Substring(3) == token);
        
        return Task.FromResult(isValid);
    }

    public Task<string> CreateClientAsync(string name)
    {
        var clientId = Guid.NewGuid().ToString();
        var token = "mt_" + Guid.NewGuid().ToString("N").Substring(0, 32);

        var client = new ClientTokenInfo
        {
            ClientId = clientId,
            Token = token,
            Name = name,
            CreatedAt = DateTime.UtcNow,
            Enabled = true
        };

        _clients.Add(client);
        SaveTokens();

        _logger.LogInformation("Created new client: {ClientId} ({Name})", clientId, name);
        return Task.FromResult(token);
    }

    public Task<bool> DisableClientAsync(string clientId)
    {
        var client = _clients.FirstOrDefault(c => c.ClientId == clientId);
        if (client == null)
        {
            return Task.FromResult(false);
        }

        client.Enabled = false;
        SaveTokens();
        _logger.LogInformation("Disabled client: {ClientId}", clientId);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteClientAsync(string clientId)
    {
        var client = _clients.FirstOrDefault(c => c.ClientId == clientId);
        if (client == null)
        {
            return Task.FromResult(false);
        }

        _clients.Remove(client);
        SaveTokens();
        _logger.LogInformation("Deleted client: {ClientId}", clientId);
        return Task.FromResult(true);
    }

    public Task<IEnumerable<ClientTokenInfo>> ListClientsAsync()
    {
        return Task.FromResult<IEnumerable<ClientTokenInfo>>(_clients.AsEnumerable());
    }

    private class TokenFileData
    {
        public List<ClientTokenInfo> Clients { get; set; } = new();
    }
}
