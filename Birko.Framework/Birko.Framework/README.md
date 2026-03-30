# Birko.Framework

Main framework application that aggregates all Birko shared projects into a single compilable .NET 10.0 application. Used for build validation, integration testing, and as a reference for consuming all framework components.

## Features

- Imports all Birko shared projects (via .projitems) for compile-time validation
- Configuration examples in `Configuration/`
- Service registration examples in `Services/`
- Usage examples in `Examples/`

## Dependencies

- All Birko shared projects (via .projitems imports)
- Microsoft.AspNetCore.App framework reference
- Provider-specific packages: Npgsql, NEST, Microsoft.Data.Sqlite, StackExchange.Redis, MQTTnet, OpenTelemetry, RazorLight, etc.

## Running

```bash
dotnet run --project Birko.Framework/Birko.Framework.csproj
```

## License

Part of the Birko Framework.
