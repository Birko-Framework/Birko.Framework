# Birko.Serialization.Tests

## Overview
Unit tests for all Birko.Serialization projects.

## Project Location
- **Directory:** `C:\Source\Birko.Serialization.Tests\`
- **Type:** Test Project (.csproj, xUnit)
- **Framework:** net10.0, xUnit 2.9.3, FluentAssertions 7.0.0

## Test Classes

### TestResources/TestPayload.cs
- `TestPayload` — Simple test model with ProtoContract attributes
- `NestedPayload` — Nested model with collections and inner object

### Core/SerializationFormatTests.cs
- Validates enum values

### Json/SystemJsonSerializerTests.cs
- ContentType, Format, round-trip (string + bytes), camelCase, null checks, nested objects, custom options

### Newtonsoft/NewtonsoftJsonSerializerTests.cs
- ContentType, Format, round-trip (string + bytes), camelCase, null checks, nested objects, custom settings

### MessagePack/MessagePackBinarySerializerTests.cs
- ContentType, Format, round-trip (bytes + Base64 string), compactness, null checks, nested objects

### Protobuf/ProtobufBinarySerializerTests.cs
- ContentType, Format, round-trip (bytes + Base64 string), null checks, nested objects

### Xml/SystemXmlSerializerTests.cs
- ContentType, Format, round-trip (string + bytes), XML structure, null checks, nested objects, custom writer settings

## Dependencies
- Birko.Serialization (projitems)
- Birko.Serialization.Newtonsoft (projitems)
- Birko.Serialization.MessagePack (projitems)
- Birko.Serialization.Protobuf (projitems)
- Newtonsoft.Json 13.0.3
- MessagePack 3.1.3
- protobuf-net 3.2.45
