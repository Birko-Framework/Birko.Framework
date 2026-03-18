# Serialization

Birko.Serialization provides a unified serialization abstraction for the Birko Framework with pluggable format-specific implementations.

## Architecture

```
Birko.Serialization          — ISerializer interface + System.Text.Json (built-in)
Birko.Serialization.Newtonsoft  — Newtonsoft.Json implementation
Birko.Serialization.MessagePack — MessagePack binary implementation
Birko.Serialization.Protobuf    — Protocol Buffers implementation
```

## ISerializer Interface

The `ISerializer` interface provides:

| Member | Description |
|--------|-------------|
| `ContentType` | MIME type (e.g., `"application/json"`, `"application/x-msgpack"`) |
| `Format` | `SerializationFormat` enum value |
| `Serialize(object)` / `Serialize<T>(T)` | Serialize to string |
| `Deserialize(string, Type)` / `Deserialize<T>(string)` | Deserialize from string |
| `SerializeToBytes(object)` / `SerializeToBytes<T>(T)` | Serialize to byte array |
| `DeserializeFromBytes(byte[], Type)` / `DeserializeFromBytes<T>(byte[])` | Deserialize from byte array |

## Implementations

### SystemJsonSerializer (built-in)

Uses `System.Text.Json`. No external dependencies.

```csharp
ISerializer serializer = new SystemJsonSerializer();

// With custom options
ISerializer serializer = new SystemJsonSerializer(new JsonSerializerOptions
{
    PropertyNamingPolicy = null,    // PascalCase
    WriteIndented = true
});
```

**Defaults:** camelCase property names, non-indented, uses `SerializeToUtf8Bytes` for efficient byte serialization.

### NewtonsoftJsonSerializer

Uses `Newtonsoft.Json`. Useful for interop with APIs requiring Newtonsoft-specific features.

**NuGet:** `Newtonsoft.Json`

```csharp
ISerializer serializer = new NewtonsoftJsonSerializer();

// With custom settings
ISerializer serializer = new NewtonsoftJsonSerializer(new JsonSerializerSettings
{
    Formatting = Formatting.Indented,
    ReferenceLoopHandling = ReferenceLoopHandling.Ignore
});
```

**Defaults:** CamelCasePropertyNamesContractResolver, NullValueHandling.Ignore, Formatting.None.

### MessagePackBinarySerializer

Uses MessagePack for compact binary serialization.

**NuGet:** `MessagePack`

```csharp
ISerializer serializer = new MessagePackBinarySerializer();

// Byte array (native, most efficient)
byte[] bytes = serializer.SerializeToBytes(myObject);
var result = serializer.DeserializeFromBytes<MyType>(bytes);

// String serialization uses Base64 encoding
string encoded = serializer.Serialize(myObject);
```

**Default:** ContractlessStandardResolver — no `[MessagePackObject]` attributes required.

### ProtobufBinarySerializer

Uses protobuf-net for Protocol Buffers serialization.

**NuGet:** `protobuf-net`

```csharp
ISerializer serializer = new ProtobufBinarySerializer();

byte[] bytes = serializer.SerializeToBytes(myObject);
var result = serializer.DeserializeFromBytes<MyType>(bytes);
```

**Requirement:** Types must be annotated with `[ProtoContract]` and `[ProtoMember]` attributes.

```csharp
[ProtoContract]
public class MyType
{
    [ProtoMember(1)]
    public string Name { get; set; } = string.Empty;

    [ProtoMember(2)]
    public int Value { get; set; }
}
```

### SystemXmlSerializer (built-in)

Uses `System.Xml.Serialization`. No external dependencies.

```csharp
ISerializer serializer = new SystemXmlSerializer();

// With custom settings
ISerializer serializer = new SystemXmlSerializer(
    writerSettings: new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true }
);
```

**Defaults:** No indentation, UTF-8 without BOM, DTD processing prohibited (secure by default).

**Requirement:** Types must be public with a parameterless constructor. Use `[XmlRoot]`, `[XmlElement]`, `[XmlAttribute]`, `[XmlArray]`, `[XmlArrayItem]`, `[XmlIgnore]` to control the XML shape.

## String Encoding for Binary Formats

Binary serializers (MessagePack, Protobuf) use **Base64 encoding** for string serialization methods. For optimal performance with binary formats, prefer `SerializeToBytes` / `DeserializeFromBytes`.

## Format Comparison

| Format | Size | Speed | Schema Required | Human-Readable |
|--------|------|-------|-----------------|----------------|
| JSON (System.Text.Json) | Largest | Good | No | Yes |
| JSON (Newtonsoft) | Largest | Good | No | Yes |
| MessagePack | Compact | Fast | No (contractless) | No |
| Protobuf | Most compact | Fastest | Yes ([ProtoContract]) | No |
| XML | Large (verbose) | Moderate | No (public types) | Yes |

## Relationship to Existing Serializers

The framework has domain-specific serializers that predate `Birko.Serialization`:

| Existing | Purpose | Can be replaced by |
|----------|---------|-------------------|
| `CacheSerializer` (Birko.Caching) | Static cache serialization | `ISerializer` via DI |
| `IJobSerializer` (Birko.BackgroundJobs) | Job input serialization | Compatible interface |
| `IMessageSerializer` (Birko.MessageQueue) | Message payload serialization | Compatible interface |

`Birko.Serialization.ISerializer` is the **unified abstraction** — new code should prefer it.
