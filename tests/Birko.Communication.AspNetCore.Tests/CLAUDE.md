# Birko.Communication.AspNetCore.Tests

## Overview

xUnit + FluentAssertions test project for `Birko.Communication.AspNetCore` (the owner-scoped minimal-API CRUD helpers).

## Project Location

`C:\Source\Birko.Communication.AspNetCore.Tests\`

## Scope

- **`OwnedCrudResultsTests`** — the pure, host-free guard functions (`ReadOwned`, `CreateClash`, `RequireOwned`): the 404-absent / 404-foreign / 409-own-id / proceed matrix, exercised without a web host.
- **`MapOwnedCrudIntegrationTests`** — the four wired routes end-to-end (`MapOwnedCrud` over a `WebApplicationFactory` / `TestServer`): create → read → update → delete happy path plus the ownership guards (foreign entity → 404, create id clash → 409, `Created` location).

## Conventions

- Regular `Microsoft.NET.Sdk` csproj with `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (required to compile the minimal-API host types). Imports `Birko.Communication.AspNetCore` `.projitems`.
- One test class per source type; test both success and guard/failure paths.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md).
