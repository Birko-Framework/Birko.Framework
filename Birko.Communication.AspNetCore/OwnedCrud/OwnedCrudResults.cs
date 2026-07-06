using System;
using Microsoft.AspNetCore.Http;

namespace Birko.Communication.AspNetCore
{
    /// <summary>
    /// The owner-scoping guard logic shared by every owner-scoped CRUD endpoint, factored out as pure
    /// functions so it is unit-testable without a web host. Each method returns an
    /// <see cref="IResult"/> (or <c>null</c> meaning "no short-circuit — proceed"), matching the shape
    /// of a minimal-API handler. This is the error-prone core (the 404-vs-409 disambiguation and the
    /// "don't leak a foreign entity's existence" rule); <see cref="OwnedCrudEndpointExtensions"/> is a
    /// thin wiring layer over it.
    /// </summary>
    public static class OwnedCrudResults
    {
        /// <summary>GET /{id}: 404 when absent or owned by someone else, else 200 + the DTO.</summary>
        public static IResult ReadOwned<TModel>(TModel? entity, Guid owner, Func<TModel, Guid?> ownerOf, Func<TModel, object> toDto)
            where TModel : class
        {
            if (entity is null || ownerOf(entity) != owner)
            {
                return Results.NotFound();
            }
            return Results.Ok(toDto(entity));
        }

        /// <summary>
        /// POST id-clash guard for an optional client-supplied id: 409 when the id already belongs to
        /// the caller, 404 when it belongs to someone else (never leak a foreign entity's existence),
        /// <c>null</c> to proceed (no clash).
        /// </summary>
        public static IResult? CreateClash<TModel>(TModel? clash, Guid owner, Func<TModel, Guid?> ownerOf, string label)
            where TModel : class
        {
            if (clash is null)
            {
                return null;
            }
            return ownerOf(clash) == owner
                ? Results.Conflict(new { error = $"A {label} with this id already exists." })
                : Results.NotFound();
        }

        /// <summary>
        /// PUT/DELETE ownership guard: yields the owned entity via <paramref name="owned"/> and returns
        /// <c>null</c> to proceed, or a 404 result when the entity is absent or owned by someone else.
        /// </summary>
        public static IResult? RequireOwned<TModel>(TModel? entity, Guid owner, Func<TModel, Guid?> ownerOf, out TModel owned)
            where TModel : class
        {
            if (entity is null || ownerOf(entity) != owner)
            {
                owned = null!;
                return Results.NotFound();
            }
            owned = entity;
            return null;
        }
    }
}
