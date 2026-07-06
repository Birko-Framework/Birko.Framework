# Birko.Communication.AspNetCore

ASP.NET Core minimal-API helpers for the Birko Framework. Ships the **owner-scoped CRUD** skeleton — the `GET /{id}`, `POST`, `PUT /{id}`, `DELETE /{id}` shape that every per-user / per-tenant resource repeats — so consumers stop hand-writing the same ownership guards (404-if-foreign, 409-vs-404 on create clash, `Created` location) on every endpoint.

## Project Location
`C:\Source\Birko.Communication.AspNetCore\` — shared project (.shproj)

## Components

- **OwnedCrud/OwnedCrudResults.cs** — the owner-scoping guard logic as **pure, host-free functions** (unit-testable without a web host). `ReadOwned` (404 when absent/foreign, else 200 + DTO), `CreateClash` (409 when the client-supplied id is the caller's, 404 when it's someone else's — never leak a foreign entity's existence), `RequireOwned` (PUT/DELETE gate yielding the owned entity or a 404). This is the error-prone core.
- **OwnedCrudMapping.cs** — `OwnedCrudMapping<TModel, TRequest, TRepo>`: the per-resource configuration. Everything domain-specific — `Owner`, `Read`, `Create`, `Update`, `Delete`, `OwnerOf`, `ToDto`, `Build`, `Apply`, optional `Validate` / `RequestGuid`, and a human `Label` — is supplied here via `required` init-only delegates.
- **OwnedCrud/OwnedCrudEndpointExtensions.cs** — `MapOwnedCrud<TModel, TRequest, TRepo>(routeBase, mapping)`: registers the four owner-scoped routes under a base path, resolving `TRepo` per request from DI. A thin wiring layer over `OwnedCrudResults` + the supplied mapping. List endpoints are intentionally out of scope (their shapes vary too much) — map those directly.

## Usage

```csharp
app.MapOwnedCrud<Exercise, ExerciseRequest, IExerciseRepository>(
    "/api/exercises",
    new OwnedCrudMapping<Exercise, ExerciseRequest, IExerciseRepository>
    {
        Owner   = ctx => ctx.User.GetUserId(),
        Read    = (repo, id) => repo.Read(id),
        Create  = (repo, e) => repo.Create(e),
        Update  = (repo, e) => repo.Update(e),
        Delete  = (repo, e) => repo.Delete(e),
        OwnerOf = e => e.OwnerId,
        ToDto   = e => new { e.Id, e.Name },
        Build   = req => new Exercise { Name = req.Name },
        Apply   = (req, e) => e.Name = req.Name,
        Label   = "exercise",
    });
```

## Dependencies

- **Microsoft.AspNetCore.App** shared framework — `IEndpointRouteBuilder`, `HttpContext`, `IResult`, `Results` (supplied by the consuming host / test project).

Shared projects carry no `PackageReference`; the importing csproj supplies the ASP.NET Core framework reference.

## Related Projects
- **Birko.Communication.REST.Server** — `HttpListener`-based REST server (framework-independent hosting)
- **Birko.Security.AspNetCore** — ASP.NET Core auth/tenant integration that supplies the `Owner` accessor (`ICurrentUser`)

## Maintenance
When modifying this project, update this README.md, CLAUDE.md, and the root CLAUDE.md / README.md.
