using Birko.Data.SQL;
using Birko.Security.OAuth.Server.Models;

namespace Birko.Security.OAuth.Server.SqLite.Stores;

/// <summary>
/// Table names of the three OAuth models stored as they are (all scalar). The models carry no <c>[Table]</c>
/// attribute, so the names are registered through <see cref="DataBase.RegisterTableName(System.Type, string)"/>, once
/// per process, before the first store is built. The two other models are stored through entities that declare
/// their tables (<c>oauth_clients</c>, <c>oauth_refresh_tokens</c>).
/// </summary>
public static class SqLiteOAuthTables
{
    public const string AuthorizationCodes = "oauth_auth_codes";
    public const string DeviceCodes = "oauth_device_codes";
    public const string Consents = "oauth_consents";

    private static readonly object Gate = new();
    private static bool _registered;

    /// <summary>Registers the table names. Idempotent; every store constructor calls it.</summary>
    public static void EnsureRegistered()
    {
        lock (Gate)
        {
            if (_registered) return;
            // Re-registering would invalidate the table cache each time, so it happens once.
            DataBase.RegisterTableName(typeof(AuthorizationCode), AuthorizationCodes);
            DataBase.RegisterTableName(typeof(DeviceCodeRecord), DeviceCodes);
            DataBase.RegisterTableName(typeof(ConsentRecord), Consents);
            _registered = true;
        }
    }
}
