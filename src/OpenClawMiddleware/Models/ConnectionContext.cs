using System.Net.WebSockets;

namespace OpenClawMiddleware.Models;

public enum ConnectionState
{
    Handshaking,
    Active,
    Idle,
    Closing
}

public class ConnectionContext
{
    public string ClientId { get; set; } = string.Empty;
    public WebSocket? Socket { get; set; }
    public byte[]? SessionKey { get; set; }
    public DateTime ConnectedAt { get; set; }
    public DateTime LastActivity { get; set; }
    public ConnectionState State { get; set; }
    
    public bool IsExpired(TimeSpan timeout)
    {
        return DateTime.UtcNow - LastActivity > timeout;
    }
}
