using System;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.Data.SQL;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;

namespace Birko.Security.OAuth.Server.SqLite.Stores;

// The three all-scalar OAuth models persist as they are through AsyncSQLiteStore<T>: the column mapper maps their
// public scalar properties, and the tables come from SqLiteOAuthTables. Tables are created on first use (the store's
// lazy schema-ensure).

/// <summary>
/// SQLite <see cref="IAuthorizationCodeStore"/> (<c>oauth_auth_codes</c>). Redemption is ONE conditional update,
/// <c>UPDATE … SET Used = 1 WHERE Guid = @g AND Used = 0</c>, so of two concurrent redemptions of one code exactly
/// one matches a row (CR-L351).
/// </summary>
public sealed class SqLiteAuthorizationCodeStore : AsyncSQLiteStore<AuthorizationCode>, IAuthorizationCodeStore
{
    public SqLiteAuthorizationCodeStore() => SqLiteOAuthTables.EnsureRegistered();

    public SqLiteAuthorizationCodeStore(PasswordSettings settings) : this() => SetSettings(settings);

    /// <inheritdoc />
    public async Task<bool> TryRedeemAsync(AuthorizationCode code, CancellationToken ct = default)
    {
        if (code.Guid is not { } guid) return false;
        await EnsureInitializedAsync(ct).ConfigureAwait(false);
        if (AsyncConnector == null) return false;
        using var _tx = EnterTransactionScope();

        Expression<Func<AuthorizationCode, bool>> unused = c => c.Guid == guid && c.Used == false;
        var wasUsed = code.Used;
        code.Used = true;
        var matched = await AsyncConnector.UpdateAsync(typeof(AuthorizationCode), code, DataBase.ParseConditionExpression(unused), ct)
            .ConfigureAwait(false);
        if (matched == 1) return true;
        code.Used = wasUsed;
        return false;
    }
}

/// <summary>SQLite <see cref="IDeviceCodeStore"/> (<c>oauth_device_codes</c>).</summary>
public sealed class SqLiteDeviceCodeStore : AsyncSQLiteStore<DeviceCodeRecord>, IDeviceCodeStore
{
    public SqLiteDeviceCodeStore() => SqLiteOAuthTables.EnsureRegistered();

    public SqLiteDeviceCodeStore(PasswordSettings settings) : this() => SetSettings(settings);
}

/// <summary>SQLite <see cref="IConsentStore"/> (<c>oauth_consents</c>).</summary>
public sealed class SqLiteConsentStore : AsyncSQLiteStore<ConsentRecord>, IConsentStore
{
    public SqLiteConsentStore() => SqLiteOAuthTables.EnsureRegistered();

    public SqLiteConsentStore(PasswordSettings settings) : this() => SetSettings(settings);
}
