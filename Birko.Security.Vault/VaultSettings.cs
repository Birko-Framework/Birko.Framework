using Birko.Configuration;

namespace Birko.Security.Vault;

/// <summary>
/// Configuration settings for HashiCorp Vault.
/// Extends <see cref="PasswordSettings"/> — Location maps to Vault address, Password maps to token.
/// </summary>
public class VaultSettings : PasswordSettings
{
    /// <summary>Vault server address. Alias for <see cref="Settings.Location"/>.</summary>
    public string Address
    {
        get => Location ?? "http://127.0.0.1:8200";
        set => Location = value;
    }

    /// <summary>Authentication token for Vault access. Alias for <see cref="PasswordSettings.Password"/>.</summary>
    public string? Token
    {
        // CR-L353: PasswordSettings.Password is non-nullable (defaults to string.Empty). Present Token as a
        // faithful string? alias — an empty backing password reads back as null (no token configured) — and
        // normalize a null assignment to string.Empty rather than forcing a null in via `value!`, which stored
        // a null the type system promised was non-null and could NRE downstream (e.g. the X-Vault-Token build).
        get => string.IsNullOrEmpty(Password) ? null : Password;
        set => Password = value ?? string.Empty;
    }

    /// <summary>KV secrets engine mount path (default: "secret"). Alias for <see cref="Settings.Name"/>.</summary>
    public string MountPath
    {
        get => Name ?? "secret";
        set => Name = value;
    }

    /// <summary>KV engine version: 1 or 2 (default: 2).</summary>
    public int KvVersion { get; set; } = 2;

    /// <summary>Optional namespace for Vault Enterprise.</summary>
    public string? Namespace { get; set; }

    /// <summary>HTTP request timeout in seconds (default: 30).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    public VaultSettings()
    {
        Location = "http://127.0.0.1:8200";
        Name = "secret";
    }

    public VaultSettings(string address, string token, string mountPath = "secret")
        : base(address, mountPath, token)
    {
    }
}
