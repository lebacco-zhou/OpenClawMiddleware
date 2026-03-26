using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using OpenClawMiddleware.Models;

namespace OpenClawMiddleware.Services;

public interface IConnectionManager
{
    void AddConnection(string clientId, ConnectionContext context);
    void RemoveConnection(string clientId);
    ConnectionContext? GetConnection(string clientId);
    IEnumerable<ConnectionContext> GetActiveConnections();
    Task BroadcastAsync(string message);
    Task SendToClientAsync(string clientId, string message);
    void CleanupIdleConnections(TimeSpan timeout);
    int GetConnectionCount();
}

public class ConnectionManager : IConnectionManager
{
    private readonly ConcurrentDictionary<string, ConnectionContext> _connections = new();
    private readonly ILogger<ConnectionManager> _logger;
    private readonly int _maxConnections;

    public ConnectionManager(ILogger<ConnectionManager> logger, IConfiguration config)
    {
        _logger = logger;
        _maxConnections = config.GetValue<int>("Server:MaxConnections", 100);
    }

    public void AddConnection(string clientId, ConnectionContext context)
    {
        if (_connections.Count >= _maxConnections)
        {
            throw new InvalidOperationException($"Maximum connection limit ({_maxConnections}) reached");
        }

        _connections[clientId] = context;
        _logger.LogInformation("Client {ClientId} connected. Total connections: {Count}", clientId, _connections.Count);
    }

    public void RemoveConnection(string clientId)
    {
        if (_connections.TryRemove(clientId, out var context))
        {
            _logger.LogInformation("Client {ClientId} disconnected. Total connections: {Count}", clientId, _connections.Count);
            context.Socket?.Dispose();
        }
    }

    public ConnectionContext? GetConnection(string clientId)
    {
        _connections.TryGetValue(clientId, out var context);
        return context;
    }

    public IEnumerable<ConnectionContext> GetActiveConnections()
    {
        return _connections.Values.Where(c => c.State == ConnectionState.Active);
    }

    public async Task BroadcastAsync(string message)
    {
        var tasks = _connections.Values
            .Where(c => c.State == ConnectionState.Active && c.Socket?.State == WebSocketState.Open)
            .Select(c => SendToClientAsync(c.ClientId, message));

        await Task.WhenAll(tasks);
    }

    public async Task SendToClientAsync(string clientId, string message)
    {
        if (_connections.TryGetValue(clientId, out var context) && 
            context.Socket?.State == WebSocketState.Open)
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            await context.Socket.SendAsync(
                new ArraySegment<byte>(bytes),
                WebSocketMessageType.Text,
                true,
                CancellationToken.None);
        }
    }

    public void CleanupIdleConnections(TimeSpan timeout)
    {
        var idleConnections = _connections.Values
            .Where(c => c.IsExpired(timeout))
            .Select(c => c.ClientId)
            .ToList();

        foreach (var clientId in idleConnections)
        {
            RemoveConnection(clientId);
        }

        if (idleConnections.Count > 0)
        {
            _logger.LogInformation("Cleaned up {Count} idle connections", idleConnections.Count);
        }
    }

    public int GetConnectionCount() => _connections.Count;
}
