# Birko.Serialization.MessagePack

## Overview
MessagePack binary serialization implementation of `ISerializer` for the Birko Framework.

## Project Location
- **Directory:** `C:\Source\Birko.Serialization.MessagePack\`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.Serialization.MessagePack`

## Components

### MessagePackBinarySerializer.cs
- `MessagePackBinarySerializer` — MessagePack implementation of `ISerializer`
  - Default: ContractlessStandardResolver (no [MessagePackObject] attributes needed)
  - String serialization: Base64-encodes the binary output
  - Byte serialization: Native MessagePack binary format
  - Configurable via `MessagePackSerializerOptions`

## Dependencies
- **Birko.Serialization** — ISerializer interface
- **MessagePack** — NuGet package (added in consuming project)

## Maintenance
Keep in sync with ISerializer interface changes in Birko.Serialization.
