using System;
using Birko.Communication.gRPC.Server;
using Grpc.AspNetCore.Server;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI extensions that register gRPC services with Birko defaults applied from <see cref="GrpcServerSettings"/>.
/// </summary>
public static class GrpcServiceExtensions
{
    /// <summary>
    /// Adds gRPC to the service collection, applying message-size and error-detail options from
    /// <paramref name="settings"/>. Equivalent to <c>services.AddGrpc(...)</c> with Birko defaults.
    /// </summary>
    /// <param name="services">The DI container.</param>
    /// <param name="settings">Optional settings; when null, library defaults are used.</param>
    /// <returns>The <see cref="IGrpcServerBuilder"/> so service registration can be chained.</returns>
    public static IGrpcServerBuilder AddBirkoGrpc(this IServiceCollection services, GrpcServerSettings? settings = null)
    {
        if (services == null)
            throw new ArgumentNullException(nameof(services));

        settings ??= new GrpcServerSettings();

        return services.AddGrpc(options =>
        {
            options.EnableDetailedErrors = settings.EnableDetailedErrors;
            options.MaxReceiveMessageSize = settings.MaxReceiveMessageSizeBytes;
            options.MaxSendMessageSize = settings.MaxSendMessageSizeBytes;
        });
    }
}
