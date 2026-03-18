# Birko.Serialization

Unified serialization abstraction for the Birko Framework. Provides a common `ISerializer` interface with a built-in System.Text.Json implementation.

## Features

- **ISerializer** — Common interface for string and byte array serialization
- **SerializationFormat** — Enum identifying the serialization format (Json, MessagePack, Protobuf, Xml)
- **SystemJsonSerializer** — Built-in JSON serializer using System.Text.Json (no external dependencies)
- **SystemXmlSerializer** — Built-in XML serializer using System.Xml.Serialization (no external dependencies)
- Configurable `JsonSerializerOptions` / `XmlWriterSettings`

## Format-Specific Projects

| Project | Format | NuGet Dependency |
|---------|--------|-----------------|
| Birko.Serialization | JSON (System.Text.Json) | None (built-in) |
| Birko.Serialization | XML (System.Xml.Serialization) | None (built-in) |
| Birko.Serialization.Newtonsoft | JSON (Newtonsoft.Json) | Newtonsoft.Json |
| Birko.Serialization.MessagePack | MessagePack | MessagePack |
| Birko.Serialization.Protobuf | Protocol Buffers | protobuf-net |

## Usage

```csharp
ISerializer serializer = new SystemJsonSerializer();

// String serialization
string json = serializer.Serialize(new { Name = "test", Value = 42 });
var result = serializer.Deserialize<MyType>(json);

// Byte array serialization
byte[] bytes = serializer.SerializeToBytes(myObject);
var obj = serializer.DeserializeFromBytes<MyType>(bytes);

// Content type for HTTP headers
string contentType = serializer.ContentType; // "application/json"
```

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
