# Birko.Health.Redis

Redis health check for the Birko Health framework.

## Features

- PING command with latency measurement
- Degraded status for high latency (>100ms)
- Connection state verification

## Usage

```csharp
// From existing connection
var check = new RedisHealthCheck(connectionMultiplexer);

// From factory
var check = new RedisHealthCheck(() => connectionManager.GetConnection());

// Register
runner.Register("redis", check, "cache", "ready");
```

## License

Part of the Birko Framework. See [License.md](License.md).
