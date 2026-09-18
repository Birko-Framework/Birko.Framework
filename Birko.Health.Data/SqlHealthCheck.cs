using System;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Health.Data;

/// <summary>
/// Health check for SQL databases. Executes a simple query to verify connectivity.
/// Works with any <see cref="DbConnection"/> (MSSql, PostgreSQL, MySQL, SQLite, TimescaleDB).
/// </summary>
public sealed class SqlHealthCheck : IHealthCheck
{
    private readonly Func<DbConnection> _connectionFactory;
    private readonly string _query;

    /// <summary>
    /// Creates a SQL health check.
    /// </summary>
    /// <param name="connectionFactory">Factory that creates a new <see cref="DbConnection"/>.</param>
    /// <param name="query">Query to execute. Defaults to "SELECT 1".</param>
    public SqlHealthCheck(Func<DbConnection> connectionFactory, string query = "SELECT 1")
    {
        _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        _query = query;
    }

    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            using var connection = _connectionFactory();
            await connection.OpenAsync(ct).ConfigureAwait(false);

            using var command = connection.CreateCommand();
            command.CommandText = _query;
            await command.ExecuteScalarAsync(ct).ConfigureAwait(false);

            return HealthCheckResult.Healthy($"SQL connection OK ({connection.DataSource}).");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy($"SQL connection failed: {ex.Message}", ex);
        }
    }
}
