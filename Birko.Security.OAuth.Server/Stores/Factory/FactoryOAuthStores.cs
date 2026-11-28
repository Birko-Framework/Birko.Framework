using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Models.Persistence;

namespace Birko.Security.OAuth.Server.Stores.Factory;

// TASK-1013: the five OAuth server stores over any IStoreFactory backend that can claim atomically (SQL, MongoDB,
// RavenDB, Cosmos DB, Elasticsearch, InMemory). Each is built from the factory once and shared (AddOAuthServerStores
// registers singletons). The three scalar models persist as they are; the client and the refresh record through their
// entities (lists and claims as JSON). The claims go through IAsyncConditionalUpdateStore<T>: of concurrent claims on one
// code or one refresh token exactly one wins, on every backend.

/// <summary>
/// <see cref="IOAuthClientStore"/> over the factory's store for <see cref="OAuthClientEntity"/>. Predicates on the
/// client's scalar members (<c>ClientId</c>, <c>Name</c>, <c>ClientType</c>, <c>IsEnabled</c>, …) are translated to the
/// entity, so <see cref="IOAuthClientStore.GetByClientIdAsync"/> and <see cref="IOAuthClientStore.GetByNameAsync"/> are
/// answered by the backend; a predicate on <c>RedirectUris</c>, <c>AllowedGrantTypes</c> or <c>AllowedScopes</c> is
/// refused (they live inside one JSON column).
/// </summary>
public sealed class FactoryOAuthClientStore : FactoryMappedStore<OAuthClient, OAuthClientEntity>, IOAuthClientStore
{
    private static readonly ModelPredicateTranslator<OAuthClient, OAuthClientEntity> Translator = new(
        nameof(FactoryOAuthClientStore),
        new Dictionary<string, Func<Expression, Expression>>
        {
            [nameof(OAuthClient.Guid)] = e => Expression.Property(e, nameof(OAuthClientEntity.Guid)),
            [nameof(OAuthClient.ClientId)] = e => Expression.Property(e, nameof(OAuthClientEntity.ClientId)),
            [nameof(OAuthClient.ClientSecretHash)] = e => Expression.Property(e, nameof(OAuthClientEntity.ClientSecretHash)),
            [nameof(OAuthClient.ClientType)] = e => Expression.Property(e, nameof(OAuthClientEntity.ClientType)),
            [nameof(OAuthClient.Name)] = e => Expression.Property(e, nameof(OAuthClientEntity.Name)),
            [nameof(OAuthClient.IsEnabled)] = e => Expression.Property(e, nameof(OAuthClientEntity.IsEnabled)),
            [nameof(OAuthClient.CreatedAt)] = e => Expression.Property(e, nameof(OAuthClientEntity.ClientCreatedAt)),
        });

    /// <exception cref="ArgumentException">The factory's backend cannot claim atomically (JSON, XML).</exception>
    public FactoryOAuthClientStore(IStoreFactory factory) : base(factory, Translator) { }

    protected override void ToEntity(OAuthClient model, OAuthClientEntity entity) => entity.CopyFrom(model);

    protected override OAuthClient ToModel(OAuthClientEntity entity) => entity.ToModel();
}

/// <summary>
/// <see cref="IRefreshTokenStore"/> over the factory's store for <see cref="RefreshTokenEntity"/>. Rotation's claim
/// (<see cref="TryRevokeAsync"/>) is one conditional update; a family is revoked with one filtered update.
/// </summary>
public sealed class FactoryRefreshTokenStore : FactoryMappedStore<RefreshTokenRecord, RefreshTokenEntity>, IRefreshTokenStore
{
    private static readonly ModelPredicateTranslator<RefreshTokenRecord, RefreshTokenEntity> Translator = new(
        nameof(FactoryRefreshTokenStore),
        new Dictionary<string, Func<Expression, Expression>>
        {
            [nameof(RefreshTokenRecord.Guid)] = e => Expression.Property(e, nameof(RefreshTokenEntity.Guid)),
            [nameof(RefreshTokenRecord.TokenHash)] = e => Expression.Property(e, nameof(RefreshTokenEntity.TokenHash)),
            [nameof(RefreshTokenRecord.ClientId)] = e => Expression.Property(e, nameof(RefreshTokenEntity.ClientId)),
            [nameof(RefreshTokenRecord.UserId)] = e => Expression.Property(e, nameof(RefreshTokenEntity.UserId)),
            [nameof(RefreshTokenRecord.Scope)] = e => Expression.Property(e, nameof(RefreshTokenEntity.Scope)),
            [nameof(RefreshTokenRecord.ExpiresAt)] = e => Expression.Property(e, nameof(RefreshTokenEntity.ExpiresAt)),
            [nameof(RefreshTokenRecord.Revoked)] = e => Expression.Property(e, nameof(RefreshTokenEntity.Revoked)),
        });

    /// <exception cref="ArgumentException">The factory's backend cannot claim atomically (JSON, XML).</exception>
    public FactoryRefreshTokenStore(IStoreFactory factory) : base(factory, Translator) { }

    protected override void ToEntity(RefreshTokenRecord model, RefreshTokenEntity entity) => entity.CopyFrom(model);

    protected override RefreshTokenRecord ToModel(RefreshTokenEntity entity) => entity.ToModel();

    /// <inheritdoc />
    public async Task<bool> TryRevokeAsync(RefreshTokenRecord record, CancellationToken ct = default)
    {
        if (record == null) throw new ArgumentNullException(nameof(record));
        if (record.Guid is not { } guid) return false;
        var won = await Claims.UpdateIfAsync(guid, e => !e.Revoked, e => e.Revoked = true, ct).ConfigureAwait(false);
        if (won == null) return false;
        await MadeVisibleAsync(ct).ConfigureAwait(false);
        record.Revoked = true;
        return true;
    }

    /// <inheritdoc />
    public async Task RevokeFamilyAsync(string clientId, string userId, CancellationToken ct = default)
    {
        await Entities.UpdateAsync(e => e.ClientId == clientId && e.UserId == userId && !e.Revoked,
            new PropertyUpdate<RefreshTokenEntity>().Set(e => e.Revoked, true), ct).ConfigureAwait(false);
        await MadeVisibleAsync(ct).ConfigureAwait(false);
    }
}

/// <summary>
/// <see cref="IAuthorizationCodeStore"/> over the factory's store for <see cref="AuthorizationCode"/>. Redemption
/// (<see cref="TryRedeemAsync"/>) is one conditional update, so of concurrent redemptions of one code exactly one wins.
/// </summary>
public sealed class FactoryAuthorizationCodeStore : FactoryModelStore<AuthorizationCode>, IAuthorizationCodeStore
{
    /// <exception cref="ArgumentException">The factory's backend cannot claim atomically (JSON, XML).</exception>
    public FactoryAuthorizationCodeStore(IStoreFactory factory) : base(factory) { }

    /// <inheritdoc />
    public async Task<bool> TryRedeemAsync(AuthorizationCode code, CancellationToken ct = default)
    {
        if (code == null) throw new ArgumentNullException(nameof(code));
        if (code.Guid is not { } guid) return false;
        var won = await Claims.UpdateIfAsync(guid, c => !c.Used, c => c.Used = true, ct).ConfigureAwait(false);
        if (won == null) return false;
        await MadeVisibleAsync(ct).ConfigureAwait(false);
        code.Used = true;
        return true;
    }
}

/// <summary><see cref="IDeviceCodeStore"/> over the factory's store for <see cref="DeviceCodeRecord"/>.</summary>
public sealed class FactoryDeviceCodeStore : FactoryModelStore<DeviceCodeRecord>, IDeviceCodeStore
{
    /// <exception cref="ArgumentException">The factory's backend cannot claim atomically (JSON, XML).</exception>
    public FactoryDeviceCodeStore(IStoreFactory factory) : base(factory) { }
}

/// <summary><see cref="IConsentStore"/> over the factory's store for <see cref="ConsentRecord"/>.</summary>
public sealed class FactoryConsentStore : FactoryModelStore<ConsentRecord>, IConsentStore
{
    /// <exception cref="ArgumentException">The factory's backend cannot claim atomically (JSON, XML).</exception>
    public FactoryConsentStore(IStoreFactory factory) : base(factory) { }
}
