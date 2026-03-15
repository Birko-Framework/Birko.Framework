using Birko.Data.Stores;

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

    public AzureKeyVaultSettings() { }

    public AzureKeyVaultSettings(string vaultUri, string tenantId, string clientId, string clientSecret)
        : base(vaultUri, tenantId, clientId, clientSecret, 443, true)
    {
    }
}
