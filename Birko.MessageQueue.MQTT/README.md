# Birko.MessageQueue.MQTT

MQTT message queue implementation for the Birko Framework using [MQTTnet](https://github.com/dotnet/MQTTnet). Provides pub/sub messaging for IoT devices, sensors, and real-time telemetry.

## Features

- **QoS levels** — At most once (0), At least once (1), Exactly once (2)
- **Persistent sessions** — `CleanSession = false` for durable subscriptions
- **Topic wildcards** — `+` (single level), `#` (multi level) with validation
- **Retained messages** — Publish with retain flag
- **Last Will and Testament** — Broker sends LWT on unexpected disconnect
- **TLS/SSL** — Encrypted connections with optional client certificates
- **Automatic reconnect** — Configurable retry with max attempts
- **Topic validation** — Static `MqttTopic` utilities for filter matching

## Usage

### Connect to broker

```csharp
var options = new MqttSettings
{
    Location = "broker.example.com",
    Port = 1883,
    ClientId = "my-device-001",
    UserName = "user",
    Password = "pass",
    AutoReconnect = true,
    DefaultQualityOfService = MqttQualityOfService.AtLeastOnce
};

var queue = new MqttMessageQueue(options);
await queue.ConnectAsync();
```

### Publish messages

```csharp
// Simple typed publish
await queue.Producer.SendAsync("sensors/temperature", new SensorReading
{
    DeviceId = "sensor-42",
    Value = 23.5,
    Unit = "°C"
});

// Publish with explicit QoS and retain
var producer = (MqttProducer)queue.Producer;
await producer.PublishAsync("devices/status/sensor-42", "online",
    qos: MqttQualityOfService.AtLeastOnce,
    retain: true);
```

### Subscribe to topics

```csharp
// Subscribe with typed handler
var sub = await queue.Consumer.SubscribeAsync<SensorReading>(
    "sensors/temperature",
    new TemperatureHandler());

// Subscribe with wildcard
var sub2 = await queue.Consumer.SubscribeAsync(
    "sensors/+/temperature",
    async (message, ct) =>
    {
        Console.WriteLine($"Reading: {message.Body}");
    });

// Multi-level wildcard
var sub3 = await queue.Consumer.SubscribeAsync(
    "devices/#",
    async (message, ct) =>
    {
        Console.WriteLine($"Device event: {message.Body}");
    });
```

### Last Will and Testament

```csharp
var options = new MqttSettings
{
    Location = "broker.example.com",
    LastWill = new MqttLastWill
    {
        Topic = "devices/status/sensor-42",
        Payload = "offline",
        QualityOfService = MqttQualityOfService.AtLeastOnce,
        Retain = true
    }
};
```

### TLS connection

```csharp
var options = new MqttSettings
{
    Location = "broker.example.com",
    Port = 8883,
    UseSecure = true,
    ClientCertificate = new X509Certificate2("client.pfx", "password")
};
```

### Topic utilities

```csharp
// Validate topics
MqttTopic.IsValidPublishTopic("sensors/temp");       // true
MqttTopic.IsValidPublishTopic("sensors/+/temp");     // false (wildcards not allowed)
MqttTopic.IsValidSubscribeFilter("sensors/+/temp");  // true
MqttTopic.IsValidSubscribeFilter("sensors/#");        // true

// Match topics against filters
MqttTopic.Matches("sensors/+/temp", "sensors/room1/temp");  // true
MqttTopic.Matches("sensors/#", "sensors/room1/temp");        // true
MqttTopic.Matches("sensors/room1", "sensors/room2");         // false
```

### Connection events

```csharp
var queue = new MqttMessageQueue(options);
queue.Connected += async () => Console.WriteLine("Connected to broker");
queue.Disconnected += async () => Console.WriteLine("Disconnected from broker");
```

## Dependencies

- **Birko.Data.Stores** — Settings hierarchy (RemoteSettings base class for MqttSettings)
- **Birko.MessageQueue** — Core interfaces
- **MQTTnet** — MQTT client library (consuming project must reference this NuGet package)

## License

[MIT](License.md)
