using System;
using System.Collections.Generic;
using System.Diagnostics;
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

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            var sw = Stopwatch.StartNew();
            var provider = _providerFactory();

            // List secrets is a lightweight connectivity check
            await provider.ListSecretsAsync(ct: ct).ConfigureAwait(false);

            sw.Stop();
            var data = new Dictionary<string, object>
            {
                ["latencyMs"] = Math.Round(sw.Elapsed.TotalMilliseconds, 2)
            };

            if (sw.Elapsed.TotalMilliseconds > 2000)
                return HealthCheckResult.Degraded($"Azure Key Vault responding slowly: {sw.Elapsed.TotalMilliseconds:F0}ms.", data: data);

            return HealthCheckResult.Healthy($"Azure Key Vault OK ({sw.Elapsed.TotalMilliseconds:F0}ms).", data);
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"Azure Key Vault failed: {ex.Message}", ex);
        }
    }
}
