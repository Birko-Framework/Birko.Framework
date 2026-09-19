# Birko.Communication.AspNetCore

## Overview

ASP.NET Core minimal-API helpers for the Birko Framework. Its one feature today is the **owner-scoped CRUD** skeleton (`OwnedCrud`) — the `GET /{id}` / `POST` / `PUT /{id}` / `DELETE /{id}` shape every per-user / per-tenant resource repeats, factored so the mechanical ownership guards live once and only the domain-specific pieces are supplied per resource.

## Project Location

`Birko.Communication.AspNetCore/`

## Components

- **`OwnedCrudResults`** (`OwnedCrud/OwnedCrudResults.cs`) — static, host-free guard functions returning `IResult` (or `null` = "proceed"), unit-testable without a web host. This is the error-prone core (404-vs-409 disambiguation, "don't leak a foreign entity's existence"):
  - `ReadOwned` — GET `/{id}`: 404 when absent or owned by someone else, else 200 + DTO.
  - `CreateClash` — POST id-clash guard for an optional client-supplied id: 409 when it's the caller's, 404 when it's a foreign entity's, `null` to proceed.
  - `RequireOwned` — PUT/DELETE gate: yields the owned entity via `out` and returns `null`, or a 404 when absent/foreign.
- **`OwnedCrudMapping<TModel, TRequest, TRepo>`** (`OwnedCrudMapping.cs`) — the per-resource config. `required` init-only delegates for the domain-specific pieces: `Owner` (caller's owner id from `HttpContext`), `Read`/`Create`/`Update`/`Delete` (repo ops), `OwnerOf` (entity's owner id), `ToDto`, `Build` (POST), `Apply` (PUT); optional `Validate` (400-style pre-check), `RequestGuid` (client-supplied id → enables the create-clash guard), and `Label` (clash-message noun, default `"resource"`).
- **`OwnedCrudEndpointExtensions`** (`OwnedCrud/OwnedCrudEndpointExtensions.cs`) — `MapOwnedCrud<TModel, TRequest, TRepo>(this IEndpointRouteBuilder, string routeBase, OwnedCrudMapping<…>)`. Registers the four routes under `routeBase` (`{id:guid}` constraint), resolving `TRepo` per request from DI. Thin wiring over `OwnedCrudResults`. List endpoints are out of scope by design.

## Dependencies

- **Microsoft.AspNetCore.App** shared framework — `IEndpointRouteBuilder`, `HttpContext`, `IResult`, `Results`, `Microsoft.Extensions.DependencyInjection` (`GetRequiredService`).

Shared project carries no `PackageReference`; the importing csproj (host / tests) supplies the ASP.NET Core framework reference.

## Conventions / notes

- **Mechanical vs domain split** — the guards + status-code discipline live in `OwnedCrudResults` / the extension; everything domain-specific goes through the mapping. Keep new endpoint helpers to the same split so the tricky part stays in one tested place.
- **Repository resolved per request** — `TRepo` is pulled from `ctx.RequestServices`, so register it in DI; the mapping delegates receive it.
- **List endpoints deliberately excluded** — their filtering/paging/projection shapes vary too much to generalise; map those directly.
- All code compiles without nullable warnings (CS8600–CS8625).

## Related Projects

- **Birko.Communication.REST.Server** — `HttpListener`-based server (framework-independent hosting; complementary, not ASP.NET).
- **Birko.Security.AspNetCore** — supplies `ICurrentUser` for the mapping's `Owner` accessor.

## Maintenance

Follow the root [CLAUDE-maintenance.md](../Birko.Framework/CLAUDE-maintenance.md). Every new public type needs xUnit + FluentAssertions tests in `Birko.Communication.AspNetCore.Tests` (`OwnedCrudResultsTests` for the pure guards, `MapOwnedCrudIntegrationTests` for the wired endpoints). When modifying this project, update this CLAUDE.md, README.md, and the root CLAUDE.md / README.md.
