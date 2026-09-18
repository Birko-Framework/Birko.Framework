# Birko.Serialization.Newtonsoft

## Overview
Newtonsoft.Json implementation of `ISerializer` for the Birko Framework.

## Project Location
- **Directory:** `C:\Source\Birko.Serialization.Newtonsoft\`
- **Type:** Shared Project (.shproj / .projitems)
- **Namespace:** `Birko.Serialization.Newtonsoft`

## Components

### NewtonsoftJsonSerializer.cs
- `NewtonsoftJsonSerializer` — Newtonsoft.Json implementation of `ISerializer`
  - Accepts optional `JsonSerializerSettings`
  - Defaults: CamelCasePropertyNamesContractResolver, NullValueHandling.Ignore, Formatting.None
  - Byte serialization via UTF-8 encoding of JSON strings

## Dependencies
- **Birko.Serialization** — ISerializer interface
- **Newtonsoft.Json** — NuGet package (added in consuming project)

## Maintenance
Keep in sync with ISerializer interface changes in Birko.Serialization.
