# Birko.Serialization.Protobuf

## Overview
Protocol Buffers binary serialization implementation of `ISerializer` for the Birko Framework.

## Project Location
- **Directory:** `C:\Source\Birko.Serialization.Protobuf\`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.Serialization.Protobuf`

## Components

### ProtobufBinarySerializer.cs
- `ProtobufBinarySerializer` — protobuf-net implementation of `ISerializer`
  - Types must use [ProtoContract] / [ProtoMember] attributes
  - String serialization: Base64-encodes the binary output
  - Byte serialization: Native protobuf binary format

## Dependencies
- **Birko.Serialization** — ISerializer interface
- **protobuf-net** — NuGet package (added in consuming project)

## Maintenance
Keep in sync with ISerializer interface changes in Birko.Serialization.
