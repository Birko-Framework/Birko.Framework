using System;
using Microsoft.AspNetCore.Http;

namespace Birko.Communication.AspNetCore
{
    /// <summary>
    /// The per-resource configuration for <see cref="OwnedCrudEndpointExtensions.MapOwnedCrud"/>: the
    /// model-specific transforms and accessors the generic owner-scoped CRUD skeleton needs. Everything
    /// mechanical (the ownership guards, the 404/409 disambiguation, the <c>Created</c> location) lives
    /// in the extension; everything domain-specific (validation, building/applying the request, the
    /// DTO shape, how "delete" is realised) is supplied here.
    /// </summary>
    /// <typeparam name="TModel">The owned entity type.</typeparam>
    /// <typeparam name="TRequest">The create/update request body type.</typeparam>
    /// <typeparam name="TRepo">The repository type, resolved per request from DI.</typeparam>
    public sealed class OwnedCrudMapping<TModel, TRequest, TRepo>
        where TModel : class
        where TRepo : notnull
    {
        /// <summary>Resolves the current caller's owner id from the request (e.g. the current user/tenant).</summary>
        public required Func<HttpContext, Guid> Owner { get; init; }

        /// <summary>Reads an entity by id from the repository (typically <c>(repo, id) =&gt; repo.Read(id)</c>).</summary>
        public required Func<TRepo, Guid, TModel?> Read { get; init; }

        /// <summary>Creates an entity, returning its assigned id.</summary>
        public required Func<TRepo, TModel, Guid> Create { get; init; }

        /// <summary>Persists an update to an existing entity.</summary>
        public required Action<TRepo, TModel> Update { get; init; }

        /// <summary>
        /// Realises a delete for an owned entity — a hard delete or a soft archive, the consumer's choice
        /// (e.g. <c>(repo, e) =&gt; repo.Delete(e)</c> or <c>(repo, e) =&gt; { e.Archived = true; repo.Update(e); }</c>).
        /// </summary>
        public required Action<TRepo, TModel> Delete { get; init; }

        /// <summary>The entity's owner id, compared against <see cref="Owner"/> for every guard.</summary>
        public required Func<TModel, Guid?> OwnerOf { get; init; }

        /// <summary>Projects an entity to its response DTO.</summary>
        public required Func<TModel, object> ToDto { get; init; }

        /// <summary>Builds a new entity from a create request (POST).</summary>
        public required Func<TRequest, TModel> Build { get; init; }

        /// <summary>Applies an update request onto an existing entity (PUT).</summary>
        public required Action<TRequest, TModel> Apply { get; init; }

        /// <summary>
        /// Optional body validation run before create/update; return a 400-style <see cref="IResult"/> to
        /// reject, or <c>null</c> to accept. Defaults to no validation.
        /// </summary>
        public Func<TRequest, IResult?>? Validate { get; init; }

        /// <summary>
        /// Optional accessor for a client-supplied id on the create body (lets a caller pin ids, e.g. a
        /// data seed). When it yields a non-empty id, the create clash guard runs. Defaults to none.
        /// </summary>
        public Func<TRequest, Guid?>? RequestGuid { get; init; }

        /// <summary>Human label for this resource, used in clash messages (e.g. "exercise"). Default "resource".</summary>
        public string Label { get; init; } = "resource";
    }
}
