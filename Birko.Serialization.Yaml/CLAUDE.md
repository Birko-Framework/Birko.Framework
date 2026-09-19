# Birko.Serialization.Yaml

## Overview
YamlDotNet implementation of `ISerializer` for the Birko Framework.

## Project Location
- **Directory:** `Birko.Serialization.Yaml/`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.Serialization.Yaml`

## Components

### YamlDotNetSerializer.cs
- `YamlDotNetSerializer` — YamlDotNet implementation of `ISerializer`
  - Accepts optional `YamlDotNet.Serialization.ISerializer` / `IDeserializer`
  - Defaults: `CamelCaseNamingConvention` on both serializer and deserializer; deserializer uses `IgnoreUnmatchedProperties()`
  - `ContentType` = `application/yaml`; `Format` = `SerializationFormat.Yaml`
  - Byte serialization via UTF-8 encoding of YAML text
  - Stream operations wrap the stream in a UTF-8 `StreamReader`/`StreamWriter` (`leaveOpen: true`); async overloads are synchronous since YamlDotNet's API is sync-only

## Dependencies
- **Birko.Serialization** — `ISerializer` interface and `SerializationFormat` enum
- **YamlDotNet** — NuGet package (added in consuming project)

## Maintenance
Keep in sync with `ISerializer` interface changes in `Birko.Serialization`.
