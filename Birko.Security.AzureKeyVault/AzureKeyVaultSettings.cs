using Birko.Configuration;

namespace Birko.Security.AzureKeyVault;

/// <summary>
/// Configuration settings for Azure Key Vault.
/// Extends <see cref="RemoteSettings"/> — Location maps to VaultUri, UserName maps to ClientId,
/// Password maps to ClientSecret, Name maps to TenantId.
/// </summary>
public class AzureKeyVaultSettings : RemoteSettings
{
    /// <summary>The Key Vault URI (e.g., "https://myvault.vault.azure.net/"). Alias for <see cref="Settings.Location"/>.</summary>
    public string VaultUri
    {
        get => Location ?? string.Empty;
        set => Location = value;
    }

    /// <summary>Azure AD tenant ID for authentication. Alias for <see cref="Settings.Name"/>.</summary>
    public string? TenantId
    {
        get => Name;
        set => Name = value!;
    }

    /// <summary>Azure AD client/application ID. Alias for <see cref="RemoteSettings.UserName"/>.</summary>
    public string? ClientId
    {
        get => UserName;
        set => UserName = value!;
    }

    /// <summary>Azure AD client secret. Alias for <see cref="PasswordSettings.Password"/>.</summary>
    public string? ClientSecret
    {
        get => Password;
        set => Password = value!;
    }

    /// <summary>HTTP request timeout in seconds (default: 30).</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Accept <c>http://</c> secret ids as well as <c>https://</c>. Default <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// A real Key Vault is always TLS, so this exists only for local emulators and test doubles.
    /// Turning it on is recorded on
    /// <see cref="AzureKeyVaultSecretProvider.InsecureSecretIdsAllowed"/> and traced as a warning at
    /// construction, because an insecure setting that nothing announces is one nobody notices.
    /// <para>
    /// It does NOT relax transport security — the vault is still reached over whatever the URI and
    /// <see cref="RemoteSettings.UseSecure"/> say. It only widens which secret ids are recognised
    /// when parsing a vault response.
    /// </para>
    /// </remarks>
    public bool AllowInsecureSecretIds { get; set; }

    public AzureKeyVaultSettings() { }

    public AzureKeyVaultSettings(string vaultUri, string tenantId, string clientId, string clientSecret)
        : base(vaultUri, tenantId, clientId, clientSecret, 443, true)
    {
    }
}
