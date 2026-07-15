using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.Security.AzureKeyVault;

namespace Birko.Health.Azure;

/// <summary>
/// Health check for Azure Key Vault connectivity.
/// Verifies the vault is accessible by listing secrets.
/// </summary>
public sealed class AzureKeyVaultHealthCheck : IHealthCheck
{
    private readonly Func<AzureKeyVaultSecretProvider> _providerFactory;

    /// <summary>
    /// Creates a health check using a factory function for the secret provider.
    /// </summary>
    public AzureKeyVaultHealthCheck(Func<AzureKeyVaultSecretProvider> providerFactory)
    {
        _providerFactory = providerFactory ?? throw new ArgumentNullException(nameof(providerFactory));
    }

    /// <summary>
    /// Creates a health check using an existing secret provider instance.
    /// </summary>
    public AzureKeyVaultHealthCheck(AzureKeyVaultSecretProvider provider)
    {
        if (provider == null) throw new ArgumentNullException(nameof(provider));
        _providerFactory = () => provider;
    }

    public Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
        // CR-L264: timing/threshold/result boilerplate lives in the shared helper.
        // List secrets is a lightweight connectivity check.
        => AzureHealthCheckHelper.MeasureAsync(
            "Azure Key Vault",
            c => _providerFactory().ListSecretsAsync(ct: c),
            ct);
}
