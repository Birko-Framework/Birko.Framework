namespace Birko.Health;

/// <summary>
/// Represents the health status of a component.
/// </summary>
public enum HealthStatus
{
    /// <summary>The component is functioning normally.</summary>
    Healthy = 0,

    /// <summary>The component is functioning but with reduced capability or performance.</summary>
    Degraded = 1,

    /// <summary>The component is not functioning.</summary>
    Unhealthy = 2
}
