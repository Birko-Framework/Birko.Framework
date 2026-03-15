using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;

namespace Birko.Telemetry;

/// <summary>
/// Extension methods for registering Birko telemetry services and middleware.
/// </summary>
public static class TelemetryServiceExtensions
{
    /// <summary>
    /// Registers Birko telemetry options into the DI container.
    /// </summary>
    public static IServiceCollection AddBirkoTelemetry(this IServiceCollection services, Action<BirkoTelemetryOptions>? configure = null)
    {
        var builder = services.AddOptions<BirkoTelemetryOptions>();
        if (configure != null)
        {
            builder.Configure(configure);
        }
        return services;
    }

    /// <summary>
    /// Adds the correlation ID middleware to the ASP.NET Core pipeline.
    /// </summary>
    public static IApplicationBuilder UseBirkoCorrelationId(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationIdMiddleware>();
    }
}
