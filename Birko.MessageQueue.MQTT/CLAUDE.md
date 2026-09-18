# Birko.MessageQueue.MQTT

## Overview
MQTT message queue implementation using MQTTnet. For IoT device communication, sensor networks, and real-time telemetry.

## Project Location
- **Directory:** `C:\Source\Birko.MessageQueue.MQTT\`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.MessageQueue.Mqtt`

## Components

| File | Description |
|------|-------------|
| MqttMessageQueue.cs | IMessageQueue implementation — connects to broker, manages reconnection, creates producer/consumer |
| MqttProducer.cs | IMessageProducer — publishes to MQTT topics with QoS and retain flag |
| MqttConsumer.cs | IMessageConsumer — subscribes to topic filters, dispatches to handlers with wildcard matching |
| MqttSubscription.cs | ISubscription — unsubscribes from MQTT topic on dispose |
| MqttSettings.cs | Connection settings extending RemoteSettings (host, port, TLS, credentials, keepalive, reconnect, LWT) |
| MqttQualityOfService.cs | Enum: AtMostOnce (0), AtLeastOnce (1), ExactlyOnce (2) |
| MqttLastWill.cs | LWT config (topic, payload, QoS, retain) |
| MqttTopic.cs | Static topic utilities — validation and wildcard matching (+/#) |

## Architecture

```
MqttMessageQueue
├── MqttProducer  -> publishes via IMqttClient
├── MqttConsumer  -> subscribes via IMqttClient, dispatches ApplicationMessageReceived events
│   └── handlers dict: topic filter -> callback (supports wildcard matching)
└── IMqttClient (MQTTnet)
    └── auto-reconnect loop on disconnect
```

- `MqttConsumer` attaches to `ApplicationMessageReceivedAsync` event once, routes messages to matching handlers
- Handler exceptions don't stop other handlers; set `MqttConsumer.OnHandlerError` to observe/log them instead of losing them silently (CR-L289)
- Wildcard matching uses `MqttTopic.Matches()` for `+` and `#` patterns; a leading `#`/`+` does **not** match `$`-prefixed system topics (e.g. `$SYS/…`), per the MQTT spec (CR-L291)
- `AcknowledgeAsync`/`RejectAsync` are no-ops — MQTT handles ack at protocol level (QoS 1/2)
- Auto-reconnect runs in background task with configurable delay and max attempts; the reconnect CTS is swapped under a lock so overlapping disconnects/Dispose don't race (CR-L290)
- `MqttSettings.LoadFrom` copies `ClientCertificate` and `LastWill` by reference (shallow) — ownership stays with the source (CR-L288)

## Dependencies
- **Birko.Data.Stores** — Settings hierarchy (RemoteSettings base class for MqttSettings)
- **Birko.MessageQueue** — Core interfaces
- **MQTTnet** — MQTT client (NuGet, referenced by consuming project)

## Important Notes
- Consuming project must add `<PackageReference Include="MQTTnet" />` to its .csproj
- MQTTnet v4+ API is used (MqttClientFactory, MqttApplicationMessageBuilder, event-based handlers)
- Topic validation: publish topics cannot contain wildcards; subscribe filters can use `+` and `#`

## Maintenance
- When adding new files, update the .projitems file
- If MQTTnet API changes in a major version, update builder patterns in MqttMessageQueue.ConnectAsync and MqttProducer
