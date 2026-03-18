# Birko.Serialization

## Overview
Unified serialization abstraction for the Birko Framework. Provides a common `ISerializer` interface and a built-in System.Text.Json implementation.

## Project Location
- **Directory:** `C:\Source\Birko.Serialization\`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.Serialization`, `Birko.Serialization.Json`, `Birko.Serialization.Xml`

## Components

### Core/ISerializer.cs
- `ISerializer` — Main serialization interface
  - `ContentType` — MIME type (e.g., "application/json")
  - `Format` — `SerializationFormat` enum value
  - `Serialize(object)` / `Serialize<T>(T)` — String serialization
  - `Deserialize(string, Type)` / `Deserialize<T>(string)` — String deserialization
  - `SerializeToBytes(object)` / `SerializeToBytes<T>(T)` — Byte array serialization
  - `DeserializeFromBytes(byte[], Type)` / `DeserializeFromBytes<T>(byte[])` — Byte array deserialization

### Core/SerializationFormat.cs
- `SerializationFormat` — Enum: `Json`, `MessagePack`, `Protobuf`, `Xml`

### Json/SystemJsonSerializer.cs
- `SystemJsonSerializer` — System.Text.Json implementation of `ISerializer`
  - Accepts optional `JsonSerializerOptions` (defaults: camelCase, non-indented)
  - Uses `JsonSerializer.SerializeToUtf8Bytes` for efficient byte serialization

### Xml/SystemXmlSerializer.cs
- `SystemXmlSerializer` — System.Xml.Serialization implementation of `ISerializer`
  - Accepts optional `XmlWriterSettings` and `XmlReaderSettings`
  - Defaults: no indentation, UTF-8 without BOM, DTD processing prohibited
  - Types must be public with parameterless constructor; use [XmlRoot], [XmlElement], etc.

## Dependencies
- None (System.Text.Json and System.Xml.Serialization are built into .NET)

## Related Projects
- **Birko.Serialization.Newtonsoft** — Newtonsoft.Json implementation
- **Birko.Serialization.MessagePack** — MessagePack implementation
- **Birko.Serialization.Protobuf** — Protobuf implementation
- **Birko.Serialization.Tests** — Unit tests

## Maintenance
When modifying the ISerializer interface, update all implementations and corresponding tests.
