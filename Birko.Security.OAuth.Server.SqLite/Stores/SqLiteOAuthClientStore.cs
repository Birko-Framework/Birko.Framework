using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Exceptions;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.SqLite.Models;
using Birko.Security.OAuth.Server.Stores;

namespace Birko.Security.OAuth.Server.SqLite.Stores;

/// <summary>
/// SQLite <see cref="IOAuthClientStore"/> over <c>oauth_clients</c>, mapping <see cref="OAuthClient"/> to
/// <see cref="OAuthClientEntity"/> (its collections as one JSON column). Rows share the model's Guid; lookups go
/// through <see cref="GetByClientIdAsync"/> (indexed).
/// </summary>
/// <remarks>
/// An arbitrary <see cref="OAuthClient"/> predicate cannot be translated to the entity's columns, so
/// <c>ReadAsync(filter)</c> and <c>CountAsync(filter)</c> with a filter are refused rather than answered from the
/// wrong column set; <c>CountAsync()</c> without one counts every client.
/// </remarks>
public sealed class SqLiteOAuthClientStore : IOAuthClientStore
{
    private sealed record Collections(List<string> RedirectUris, List<string> AllowedGrantTypes, List<string> AllowedScopes);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly AsyncSQLiteStore<OAuthClientEntity> _entities = new();

    public SqLiteOAuthClientStore(PasswordSettings settings)
    {
        _entities.SetSettings(settings ?? throw new ArgumentNullException(nameof(settings)));
    }

    private static void CopyToEntity(OAuthClient client, OAuthClientEntity entity)
    {
        entity.ClientId = client.ClientId;
        entity.ClientSecretHash = client.ClientSecretHash;
        entity.ClientType = (int)client.ClientType;
        entity.Name = client.Name;
        entity.IsEnabled = client.IsEnabled;
        entity.ClientCreatedAt = client.CreatedAt;
        entity.CollectionsJson = JsonSerializer.Serialize(
            new Collections(client.RedirectUris, client.AllowedGrantTypes, client.AllowedScopes), JsonOptions);
    }

    private static OAuthClient ToModel(OAuthClientEntity entity)
    {
        var collections = JsonSerializer.Deserialize<Collections>(entity.CollectionsJson, JsonOptions)
            ?? new Collections(new(), new(), new());
        return new OAuthClient
        {
            Guid = entity.Guid,
            ClientId = entity.ClientId,
            ClientSecretHash = entity.ClientSecretHash,
            ClientType = (OAuthClientType)entity.ClientType,
            Name = entity.Name,
            IsEnabled = entity.IsEnabled,
            CreatedAt = entity.ClientCreatedAt,
            RedirectUris = collections.RedirectUris ?? new(),
            AllowedGrantTypes = collections.AllowedGrantTypes ?? new(),
            AllowedScopes = collections.AllowedScopes ?? new(),
        };
    }

    /// <inheritdoc />
    public async Task<OAuthClient?> GetByClientIdAsync(string clientId, CancellationToken ct = default)
    {
        var entity = await _entities.ReadAsync(e => e.ClientId == clientId, ct).ConfigureAwait(false);
        return entity == null ? null : ToModel(entity);
    }

    public Task InitAsync(CancellationToken ct = default) => _entities.InitAsync(ct);

    public Task DestroyAsync(CancellationToken ct = default) => _entities.DestroyAsync(ct);

    public Task<long> CountAsync(Expression<Func<OAuthClient, bool>>? filter = null, CancellationToken ct = default)
    {
        if (filter != null)
            throw new NotSupportedException("SqLiteOAuthClientStore cannot translate an OAuthClient predicate to its table; count without a filter.");
        return _entities.CountAsync((Expression<Func<OAuthClientEntity, bool>>?)null, ct);
    }

    public async Task<OAuthClient?> ReadAsync(Guid guid, CancellationToken ct = default)
    {
        var entity = await _entities.ReadAsync(guid, ct).ConfigureAwait(false);
        return entity == null ? null : ToModel(entity);
    }

    public async Task<OAuthClient?> ReadAsync(Expression<Func<OAuthClient, bool>>? filter = null, CancellationToken ct = default)
    {
        if (filter != null)
            throw new NotSupportedException("SqLiteOAuthClientStore cannot translate an OAuthClient predicate to its table; use GetByClientIdAsync or ReadAsync(guid).");
        var entity = await _entities.ReadAsync((Expression<Func<OAuthClientEntity, bool>>?)null, ct).ConfigureAwait(false);
        return entity == null ? null : ToModel(entity);
    }

    public async Task<Guid> CreateAsync(OAuthClient data, StoreDataDelegate<OAuthClient>? processDelegate = null, CancellationToken ct = default)
    {
        data = StoreDelegates.Apply(processDelegate, data);
        var entity = new OAuthClientEntity { Guid = data.Guid is { } g && g != Guid.Empty ? g : Guid.NewGuid() };
        CopyToEntity(data, entity);
        var guid = await _entities.CreateAsync(entity, ct: ct).ConfigureAwait(false);
        data.Guid = guid;
        return guid;
    }

    /// <summary>Updates the stored client with <paramref name="data"/>'s Guid; <see cref="EntityNotFoundException"/> when none is stored.</summary>
    public async Task UpdateAsync(OAuthClient data, StoreDataDelegate<OAuthClient>? processDelegate = null, CancellationToken ct = default)
    {
        data = StoreDelegates.Apply(processDelegate, data);
        var existing = data.Guid is { } guid ? await _entities.ReadAsync(guid, ct).ConfigureAwait(false) : null;
        if (existing == null) throw new EntityNotFoundException(typeof(OAuthClient), data.Guid);
        CopyToEntity(data, existing);
        await _entities.UpdateAsync(existing, ct: ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(OAuthClient data, CancellationToken ct = default)
    {
        var existing = data.Guid is { } guid ? await _entities.ReadAsync(guid, ct).ConfigureAwait(false) : null;
        if (existing != null)
            await _entities.DeleteAsync(existing, ct).ConfigureAwait(false);
    }

    /// <summary>Creates the client, or updates the stored one with the same Guid.</summary>
    public async Task<Guid> SaveAsync(OAuthClient data, StoreDataDelegate<OAuthClient>? processDelegate = null, CancellationToken ct = default)
    {
        data = StoreDelegates.Apply(processDelegate, data);
        if (data.Guid is { } guid && guid != Guid.Empty && await _entities.ReadAsync(guid, ct).ConfigureAwait(false) != null)
        {
            await UpdateAsync(data, ct: ct).ConfigureAwait(false);
            return guid;
        }
        return await CreateAsync(data, ct: ct).ConfigureAwait(false);
    }

    public OAuthClient CreateInstance() => new();
}
