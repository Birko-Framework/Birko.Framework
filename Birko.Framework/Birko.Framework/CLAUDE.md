# Birko.Framework

## Overview
Main framework application (.NET 10.0) that aggregates all Birko shared projects into a single compilable project. Serves as build validation and reference implementation for consuming all framework components together.

## Project Location
`C:\Source\Birko.Framework\Birko.Framework\`

## Structure
- `Program.cs` - Application entry point
- `Configuration/` - Configuration examples
- `Services/` - Service registration examples
- `Examples/` - Usage examples
- `appsettings.json` - Application settings

## Dependencies
This project imports virtually all Birko shared projects via .projitems, including:
- All Data layer projects (SQL, ElasticSearch, MongoDB, RavenDB, InfluxDB, TimescaleDB, JSON)
- All ViewModel projects
- All Communication projects
- All Security projects
- Caching, Messaging, Telemetry, BackgroundJobs, MessageQueue, EventBus
- Storage, Health, Rules, CQRS, Workflow, Serialization
- Models, Validation, Structures, Helpers

External packages: Npgsql, NEST, Microsoft.Data.Sqlite, StackExchange.Redis, MQTTnet, OpenTelemetry, RazorLight, Newtonsoft.Json, MessagePack, protobuf-net, etc.

## Key Notes
- OutputType is Exe — this is a runnable console application
- Uses `$(MSBuildThisFileDirectory)` prefix for all shared project imports to ensure paths work from both CLI and Visual Studio
- NuGet audit suppressions for known transitive vulnerabilities from Microsoft.Data.SqlClient
- PreserveCompilationContext is enabled for RazorLight template compilation

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns, update README.md.

### CLAUDE.md Updates
When making major changes, update this CLAUDE.md to reflect new or renamed files, changed architecture, or updated dependencies.
