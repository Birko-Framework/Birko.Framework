namespace Birko.Telemetry;

/// <summary>
/// Configuration options for Birko telemetry.
/// </summary>
public class BirkoTelemetryOptions
{
    /// <summary>
    /// Gets or sets whether the correlation ID middleware is enabled. Default: true.
    /// </summary>
    public bool EnableCorrelationId { get; set; } = true;

    /// <summary>
    /// Gets or sets the HTTP header name used for correlation IDs.
    /// Default: <see cref="BirkoTelemetryConventions.DefaultCorrelationIdHeader"/>.
    /// </summary>
    public string CorrelationIdHeaderName { get; set; } = BirkoTelemetryConventions.DefaultCorrelationIdHeader;
}
