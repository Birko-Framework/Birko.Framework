namespace Birko.Framework.Configuration
{
    /// <summary>
    /// Main framework configuration options
    /// </summary>
    public class FrameworkOptions
    {
        public string Name { get; set; } = "Birko Framework";
        public string Version { get; set; } = "1.0.0";
        public string Environment { get; set; } = "Development";
    }

    /// <summary>
    /// Data layer configuration options
    /// </summary>
    public class DataOptions
    {
        public string DefaultStoreType { get; set; } = "Json";
        public string ConnectionString { get; set; } = "./Data";
        public bool EnableChangeTracking { get; set; } = true;
    }

    /// <summary>
    /// Communication layer configuration options
    /// </summary>
    public class CommunicationOptions
    {
        public WebSocketOptions WebSocket { get; set; } = new();
        public SseOptions SSE { get; set; } = new();
    }

    /// <summary>
    /// WebSocket configuration options
    /// </summary>
    public class WebSocketOptions
    {
        public bool Enabled { get; set; } = false;
        public int Port { get; set; } = 8080;
    }

    /// <summary>
    /// Server-Sent Events (SSE) configuration options
    /// </summary>
    public class SseOptions
    {
        public bool Enabled { get; set; } = false;
        public int Port { get; set; } = 8081;
        public string Path { get; set; } = "/sse";
        public int KeepAliveIntervalSeconds { get; set; } = 30;
        public int ReconnectIntervalSeconds { get; set; } = 5;
        public bool EnableAuthentication { get; set; } = true;
        public List<string>? AllowedTokens { get; set; }
        public bool EnableCors { get; set; } = true;
        public List<string>? AllowedOrigins { get; set; }
    }
}
