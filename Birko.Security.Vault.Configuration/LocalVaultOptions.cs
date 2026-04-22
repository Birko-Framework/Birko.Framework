namespace Birko.Security.Vault.Configuration;

/// <summary>
/// Runtime inputs for <see cref="LocalVaultConfigurationExtensions.AddLocalVaultConfiguration"/>.
/// <para>
/// All properties are optional — defaults are library-neutral. Callers supply the scope
/// either through the extension method's parameters, environment variables
/// (<c>LOCAL_VAULT_TOKEN</c>, <c>LOCAL_VAULT_ADDR</c>, <c>LOCAL_VAULT_USER</c>,
/// <c>LOCAL_VAULT_DOMAIN</c>, <c>LOCAL_VAULT_ENVIRONMENT</c>), or by instantiating this
/// type directly and passing it to the extension.
/// </para>
/// </summary>
public sealed class LocalVaultOptions
{
    /// <summary>
    /// Vault token used for authentication. Required — empty values cause the provider
    /// to log a warning and no-op.
    /// </summary>
    public string Token { get; set; } = string.Empty;

    /// <summary>Vault base URL. Falls back to HashiCorp's dev-server default.</summary>
    public string Url { get; set; } = "http://localhost:8200";

    /// <summary>
    /// Optional user name. When set, enables user-scoped override paths
    /// <c>&lt;mount&gt;/&lt;domain&gt;/users/&lt;user&gt;/&lt;project&gt;[.env]</c>.
    /// </summary>
    public string User { get; set; } = string.Empty;

    /// <summary>
    /// Tenant / namespace segment inside the KV mount (e.g. company code, product line).
    /// Empty by default — callers set this explicitly for their environment.
    /// </summary>
    public string Domain { get; set; } = string.Empty;

    /// <summary>
    /// Environment suffix appended to path variants (e.g. <c>development</c>, <c>staging</c>,
    /// <c>production</c>). Empty skips the <c>project.env</c> and
    /// <c>user/project.env</c> variants.
    /// </summary>
    public string Environment { get; set; } = string.Empty;

    /// <summary>KV secrets engine mount path (default: <c>kv</c>).</summary>
    public string MountPath { get; set; } = "kv";

    /// <summary>KV engine version: 1 or 2 (default: 2).</summary>
    public int KvVersion { get; set; } = 2;

    /// <summary>HTTP request timeout in seconds (default: 30).</summary>
    public int TimeoutSeconds { get; set; } = 30;
}
