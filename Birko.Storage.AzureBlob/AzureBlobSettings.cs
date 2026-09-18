using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Storage.AzureBlob;

/// <summary>
/// Configuration settings for Azure Blob Storage.
/// Extends <see cref="RemoteSettings"/> — Location maps to StorageAccountUri, UserName maps to ClientId,
/// Password maps to ClientSecret, Name maps to TenantId.
/// </summary>
public class AzureBlobSettings : RemoteSettings
{
    /// <summary>
    /// The storage account URI (e.g., "https://myaccount.blob.core.windows.net").
    /// Alias for <see cref="Settings.Location"/>.
    /// </summary>
    public string StorageAccountUri
    {
        get => Location ?? string.Empty;
        set => Location = value;
    }

    /// <summary>Azure AD tenant ID for OAuth2 authentication. Alias for <see cref="Settings.Name"/>.</summary>
    public string? TenantId
    {
        get => Name;
        // CR-L373: base Name is a non-nullable string; coalesce null → empty instead of planting a null
        // behind the non-null contract with `value!`.
        set => Name = value ?? string.Empty;
    }

    /// <summary>Azure AD client/application ID. Alias for <see cref="RemoteSettings.UserName"/>.</summary>
    public string? ClientId
    {
        get => UserName;
        set => UserName = value ?? string.Empty; // CR-L373
    }

    /// <summary>Azure AD client secret. Alias for <see cref="PasswordSettings.Password"/>.</summary>
    public string? ClientSecret
    {
        // CR-L373: base Password defaults to string.Empty (not null!), so normalize empty → null here to
        // present the same "unset reads back as null" contract as TenantId/ClientId.
        get => string.IsNullOrEmpty(Password) ? null : Password;
        set => Password = value ?? string.Empty; // CR-L373
    }

    /// <summary>
    /// Default container name. Required for all operations.
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;

    /// <summary>
    /// HTTP request timeout in seconds (default: 30).
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Optional path prefix for tenant isolation (e.g., "tenant-123/").
    /// Applied to all blob paths.
    /// </summary>
    public string? PathPrefix { get; set; }

    /// <summary>
    /// Default storage options applied to uploads when not overridden per-call.
    /// </summary>
    public StorageOptions? DefaultOptions { get; set; }

    public AzureBlobSettings() { }

    public AzureBlobSettings(
        string storageAccountUri,
        string containerName,
        string tenantId,
        string clientId,
        string clientSecret,
        string? pathPrefix = null)
        : base(storageAccountUri, tenantId, clientId, clientSecret, 443, true)
    {
        ContainerName = containerName;
        PathPrefix = pathPrefix;
    }
}
