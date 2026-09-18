# Birko.Messaging.Razor.Tests

## Overview

Unit tests for Birko.Messaging.Razor — Razor template engine for the Birko Messaging framework.

## Project Location

- **Path:** `C:\Source\Birko.Messaging.Razor.Tests\`
- **Type:** Test project (`.csproj`, xUnit)
- **Target:** net10.0

## Components

### RazorTemplateOptionsTests.cs
- Tests default property values
- Tests custom property assignment

### RazorFileTemplateProviderTests.cs
- File loading (with/without extension, subdirectories)
- In-memory caching behavior
- Cache invalidation (single + clear all)
- Directory traversal protection
- Constructor validation (null options, null base path)
- Error handling for missing files

### RazorTemplateEngineTests.cs
- Inline Razor rendering (simple model, conditionals, loops)
- IMessageTemplate rendering (file-based + inline fallback)
- File-based rendering via RenderFileAsync
- Cache reuse for same template content
- Null argument validation
- Invalid Razor syntax error handling
- Dispose behavior (idempotent, ObjectDisposedException after dispose)
- Constructor validation

### TestResources/TestTemplate.cs
- Simple IMessageTemplate implementation for testing

## Dependencies

- **Birko.Data.Core** (.projitems) — foundation types
- **Birko.Data.Stores** (.projitems) — Settings base classes
- **Birko.Messaging** (.projitems) — ITemplateEngine, IMessageTemplate, exceptions
- **Birko.Messaging.Razor** (.projitems) — code under test
- **RazorLight** 2.3.1 (NuGet) — Razor compilation engine
- **xUnit** 2.9.3, **FluentAssertions** 7.0.0

## Conventions

- Tests use temp directories for file-based tests, cleaned up via IDisposable
- Test class naming: `{ClassName}Tests`
- Test method naming: `MethodName_Scenario_ExpectedBehavior`

## Maintenance

- When adding new features to Birko.Messaging.Razor, add corresponding tests here
- Keep test coverage for both success and failure paths
