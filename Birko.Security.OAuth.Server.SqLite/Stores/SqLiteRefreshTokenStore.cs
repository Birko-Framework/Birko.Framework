using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.Exceptions;
using Birko.Data.SQL;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.SqLite.Models;
using Birko.Security.OAuth.Server.Stores;

namespace Birko.Security.OAuth.Server.SqLite.Stores;

/// <summary>
/// SQLite <see cref="IRefreshTokenStore"/> over <c>oauth_refresh_tokens</c>, mapping <see cref="RefreshTokenRecord"/>
/// to <see cref="RefreshTokenEntity"/> so the record's <c>Claims</c> survive (as a JSON column).
/// </summary>
/// <remarks>
/// <para>
/// Rotation's claim, <see cref="TryRevokeAsync"/>, is ONE conditional update
/// (<c>… SET Revoked = 1 WHERE Guid = @g AND Revoked = 0</c>), so of two concurrent refreshes with one token exactly one
/// gets a new pair. <see cref="RevokeFamilyAsync"/> is one set-based update.
/// </para>
/// <para>
/// An arbitrary <see cref="RefreshTokenRecord"/> predicate cannot be translated to the entity's columns, so
/// <c>ReadAsync(filter)</c> and <c>CountAsync(filter)</c> with a filter are refused; the server only needs
/// <see cref="GetByHashAsync"/>.
/// </para>
/// </remarks>
public sealed class SqLiteRefreshTokenStore : IRefreshTokenStore
{
    /// <summary>The entity store, plus the conditional update the public SQLite store does not expose.</summary>
    private sealed class EntityStore : AsyncSQLiteStore<RefreshTokenEntity>
    {
        public async Task<bool> RevokeIfActiveAsync(Guid guid, CancellationToken ct)
        {
            var entity = await ReadAsync(guid, ct).ConfigureAwait(false);
            if (entity == null || entity.Revoked || AsyncConnector == null) return false;
            using var _tx = EnterTransactionScope();
            entity.Revoked = true;
            Expression<Func<RefreshTokenEntity, bool>> active = e => e.Guid == guid && e.Revoked == false;
            var matched = await AsyncConnector.UpdateAsync(typeof(RefreshTokenEntity), entity, DataBase.ParseConditionExpression(active), ct)
                .ConfigureAwait(false);
            return matched == 1;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new();

    private readonly EntityStore _entities = new();

    public SqLiteRefreshTokenStore(PasswordSettings settings)
    {
        _entities.SetSettings(settings ?? throw new ArgumentNullException(nameof(settings)));
    }

    private static void CopyToEntity(RefreshTokenRecord record, RefreshTokenEntity entity)
    {
        entity.TokenHash = record.TokenHash;
        entity.ClientId = record.ClientId;
        entity.UserId = record.UserId;
        entity.Scope = record.Scope;
        entity.ExpiresAt = record.ExpiresAt;
        entity.Revoked = record.Revoked;
        entity.ClaimsJson = JsonSerializer.Serialize(record.Claims ?? new Dictionary<string, string>(), JsonOptions);
    }

    private static RefreshTokenRecord ToModel(RefreshTokenEntity entity) => new()
    {
        Guid = entity.Guid,
        TokenHash = entity.TokenHash,
        ClientId = entity.ClientId,
        UserId = entity.UserId,
        Scope = entity.Scope,
        ExpiresAt = entity.ExpiresAt,
        Revoked = entity.Revoked,
        Claims = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ClaimsJson, JsonOptions) ?? new(),
    };

    /// <inheritdoc />
    public async Task<RefreshTokenRecord?> GetByHashAsync(string tokenHash, CancellationToken ct = default)
    {
        var entity = await _entities.ReadAsync(e => e.TokenHash == tokenHash, ct).ConfigureAwait(false);
        return entity == null ? null : ToModel(entity);
    }

    /// <inheritdoc />
    public async Task<bool> TryRevokeAsync(RefreshTokenRecord record, CancellationToken ct = default)
    {
        if (record.Guid is not { } guid) return false;
        if (!await _entities.RevokeIfActiveAsync(guid, ct).ConfigureAwait(false)) return false;
        record.Revoked = true;
        return true;
    }

    /// <inheritdoc />
    public Task RevokeFamilyAsync(string clientId, string userId, CancellationToken ct = default)
        => _entities.UpdateAsync(e => e.ClientId == clientId && e.UserId == userId && e.Revoked == false,
            new PropertyUpdate<RefreshTokenEntity>().Set(e => e.Revoked, true), ct);

    public Task InitAsync(CancellationToken ct = default) => _entities.InitAsync(ct);

    public Task DestroyAsync(CancellationToken ct = default) => _entities.DestroyAsync(ct);

    public Task<long> CountAsync(Expression<Func<RefreshTokenRecord, bool>>? filter = null, CancellationToken ct = default)
    {
        if (filter != null)
            throw new NotSupportedException("SqLiteRefreshTokenStore cannot translate a RefreshTokenRecord predicate to its table; count without a filter.");
        return _entities.CountAsync((Expression<Func<RefreshTokenEntity, bool>>?)null, ct);
    }

    public async Task<RefreshTokenRecord?> ReadAsync(Guid guid, CancellationToken ct = default)
    {
        var entity = await _entities.ReadAsync(guid, ct).ConfigureAwait(false);
        return entity == null ? null : ToModel(entity);
    }

    public async Task<RefreshTokenRecord?> ReadAsync(Expression<Func<RefreshTokenRecord, bool>>? filter = null, CancellationToken ct = default)
    {
        if (filter != null)
            throw new NotSupportedException("SqLiteRefreshTokenStore cannot translate a RefreshTokenRecord predicate to its table; use GetByHashAsync or ReadAsync(guid).");
        var entity = await _entities.ReadAsync((Expression<Func<RefreshTokenEntity, bool>>?)null, ct).ConfigureAwait(false);
        return entity == null ? null : ToModel(entity);
    }

    public async Task<Guid> CreateAsync(RefreshTokenRecord data, StoreDataDelegate<RefreshTokenRecord>? processDelegate = null, CancellationToken ct = default)
    {
        data = StoreDelegates.Apply(processDelegate, data);
        var entity = new RefreshTokenEntity { Guid = data.Guid is { } g && g != Guid.Empty ? g : Guid.NewGuid() };
        CopyToEntity(data, entity);
        var guid = await _entities.CreateAsync(entity, ct: ct).ConfigureAwait(false);
        data.Guid = guid;
        return guid;
    }

    /// <summary>Updates the stored record with <paramref name="data"/>'s Guid; <see cref="EntityNotFoundException"/> when none is stored.</summary>
    public async Task UpdateAsync(RefreshTokenRecord data, StoreDataDelegate<RefreshTokenRecord>? processDelegate = null, CancellationToken ct = default)
    {
        data = StoreDelegates.Apply(processDelegate, data);
        if (data.Guid is not { } guid) throw new EntityNotFoundException(typeof(RefreshTokenRecord), (Guid?)null);
        var entity = new RefreshTokenEntity { Guid = guid };
        CopyToEntity(data, entity);
        try
        {
            await _entities.UpdateAsync(entity, ct: ct).ConfigureAwait(false);
        }
        catch (EntityNotFoundException)
        {
            throw new EntityNotFoundException(typeof(RefreshTokenRecord), guid);
        }
    }

    public async Task DeleteAsync(RefreshTokenRecord data, CancellationToken ct = default)
    {
        if (data.Guid is not { } guid) return;
        var entity = await _entities.ReadAsync(guid, ct).ConfigureAwait(false);
        if (entity != null)
            await _entities.DeleteAsync(entity, ct).ConfigureAwait(false);
    }

    /// <summary>Creates the record, or updates the stored one with the same Guid.</summary>
    public async Task<Guid> SaveAsync(RefreshTokenRecord data, StoreDataDelegate<RefreshTokenRecord>? processDelegate = null, CancellationToken ct = default)
    {
        data = StoreDelegates.Apply(processDelegate, data);
        if (data.Guid is { } guid && guid != Guid.Empty && await _entities.ReadAsync(guid, ct).ConfigureAwait(false) != null)
        {
            await UpdateAsync(data, ct: ct).ConfigureAwait(false);
            return guid;
        }
        return await CreateAsync(data, ct: ct).ConfigureAwait(false);
    }

    public RefreshTokenRecord CreateInstance() => new();
}
