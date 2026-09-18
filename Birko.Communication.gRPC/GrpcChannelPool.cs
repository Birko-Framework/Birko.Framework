using System.Collections.Concurrent;
using Grpc.Net.Client;

namespace Birko.Communication.gRPC;

/// <summary>
/// Endpoint-keyed pool of reusable <see cref="GrpcChannel"/> instances.
/// <para>
/// gRPC channels are expensive to create and are safe to share across calls and threads,
/// so callers should reuse a single channel per endpoint rather than creating one per request.
/// This pool mirrors the caching pattern used by <c>RestClient.GetClient</c>.
/// </para>
/// </summary>
public static class GrpcChannelPool
{
    private static readonly ConcurrentDictionary<string, GrpcChannel> _channels = new();

    /// <summary>
    /// Gets (or creates and caches) the channel for the given settings, keyed by <see cref="GrpcSettings.Endpoint"/>.
    /// </summary>
    public static GrpcChannel GetChannel(GrpcSettings settings)
    {
        if (settings == null)
            throw new System.ArgumentNullException(nameof(settings));
        if (string.IsNullOrWhiteSpace(settings.Endpoint))
            throw new System.ArgumentException("Endpoint must be set.", nameof(settings));

        return _channels.GetOrAdd(settings.Endpoint, _ => CreateChannel(settings));
    }

    private static GrpcChannel CreateChannel(GrpcSettings settings)
    {
        var options = new GrpcChannelOptions();

        if (settings.MaxReceiveMessageSizeBytes.HasValue)
            options.MaxReceiveMessageSize = settings.MaxReceiveMessageSizeBytes;
        if (settings.MaxSendMessageSizeBytes.HasValue)
            options.MaxSendMessageSize = settings.MaxSendMessageSizeBytes;
        if (settings.Credentials != null)
            options.Credentials = settings.Credentials;

        return GrpcChannel.ForAddress(settings.Endpoint, options);
    }

    /// <summary>
    /// Removes and disposes the channel cached for the given endpoint.
    /// </summary>
    /// <returns><c>true</c> if a channel was removed; otherwise <c>false</c>.</returns>
    public static bool Remove(string endpoint)
    {
        if (_channels.TryRemove(endpoint, out var channel))
        {
            channel.Dispose();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Disposes and clears every pooled channel.
    /// </summary>
    public static void Clear()
    {
        foreach (var channel in _channels.Values)
        {
            channel.Dispose();
        }
        _channels.Clear();
    }
}
