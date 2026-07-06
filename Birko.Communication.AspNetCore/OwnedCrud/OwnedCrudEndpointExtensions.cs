using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Birko.Communication.AspNetCore
{
    /// <summary>
    /// Maps the owner-scoped CRUD skeleton — <c>GET /{id}</c>, <c>POST</c>, <c>PUT /{id}</c>,
    /// <c>DELETE /{id}</c> — that every owner-scoped resource repeats, so consumers stop re-writing the
    /// same ownership guards (404-if-foreign, 409-vs-404 on create clash, <c>Created</c> location) by
    /// hand. The mechanical guards come from <see cref="OwnedCrudResults"/>; the domain-specific pieces
    /// come from the supplied <see cref="OwnedCrudMapping{TModel,TRequest,TRepo}"/>.
    /// </summary>
    public static class OwnedCrudEndpointExtensions
    {
        /// <summary>
        /// Registers the four owner-scoped CRUD routes under <paramref name="routeBase"/> (e.g.
        /// <c>"/api/exercises"</c>). The repository <typeparamref name="TRepo"/> is resolved per request
        /// from DI, so register it in the container. List endpoints are intentionally out of scope
        /// (their shapes vary too much) — map those directly.
        /// </summary>
        public static IEndpointRouteBuilder MapOwnedCrud<TModel, TRequest, TRepo>(
            this IEndpointRouteBuilder endpoints,
            string routeBase,
            OwnedCrudMapping<TModel, TRequest, TRepo> map)
            where TModel : class
            where TRepo : notnull
        {
            if (endpoints is null)
            {
                throw new ArgumentNullException(nameof(endpoints));
            }
            if (string.IsNullOrWhiteSpace(routeBase))
            {
                throw new ArgumentException("Route base is required.", nameof(routeBase));
            }
            if (map is null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            var basePath = "/" + routeBase.Trim('/');

            endpoints.MapGet(basePath + "/{id:guid}", IResult (Guid id, HttpContext ctx) =>
            {
                var repo = ctx.RequestServices.GetRequiredService<TRepo>();
                return OwnedCrudResults.ReadOwned(map.Read(repo, id), map.Owner(ctx), map.OwnerOf, map.ToDto);
            });

            endpoints.MapPost(basePath, IResult (TRequest req, HttpContext ctx) =>
            {
                var repo = ctx.RequestServices.GetRequiredService<TRepo>();
                var owner = map.Owner(ctx);

                var invalid = map.Validate?.Invoke(req);
                if (invalid is not null)
                {
                    return invalid;
                }

                var requestId = map.RequestGuid?.Invoke(req);
                if (requestId is { } rid && rid != Guid.Empty)
                {
                    var clash = OwnedCrudResults.CreateClash(map.Read(repo, rid), owner, map.OwnerOf, map.Label);
                    if (clash is not null)
                    {
                        return clash;
                    }
                }

                var model = map.Build(req);
                var newId = map.Create(repo, model);
                var created = map.Read(repo, newId);
                return Results.Created($"{basePath}/{newId}", created is null ? null : map.ToDto(created));
            });

            endpoints.MapPut(basePath + "/{id:guid}", IResult (Guid id, TRequest req, HttpContext ctx) =>
            {
                var repo = ctx.RequestServices.GetRequiredService<TRepo>();
                var guard = OwnedCrudResults.RequireOwned(map.Read(repo, id), map.Owner(ctx), map.OwnerOf, out var existing);
                if (guard is not null)
                {
                    return guard;
                }

                var invalid = map.Validate?.Invoke(req);
                if (invalid is not null)
                {
                    return invalid;
                }

                map.Apply(req, existing);
                map.Update(repo, existing);
                return Results.Ok(map.ToDto(existing));
            });

            endpoints.MapDelete(basePath + "/{id:guid}", IResult (Guid id, HttpContext ctx) =>
            {
                var repo = ctx.RequestServices.GetRequiredService<TRepo>();
                var guard = OwnedCrudResults.RequireOwned(map.Read(repo, id), map.Owner(ctx), map.OwnerOf, out var existing);
                if (guard is not null)
                {
                    return guard;
                }

                map.Delete(repo, existing);
                return Results.NoContent();
            });

            return endpoints;
        }
    }
}
