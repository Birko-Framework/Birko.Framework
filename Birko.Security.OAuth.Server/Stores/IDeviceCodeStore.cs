using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Stores;
using Birko.Security.OAuth.Server.Models;

namespace Birko.Security.OAuth.Server.Stores;

/// <summary>
/// Persistence for in-flight RFC 8628 device authorization requests.
/// </summary>
public interface IDeviceCodeStore : IAsyncStore<DeviceCodeRecord>
{
    Task<DeviceCodeRecord?> GetByDeviceCodeAsync(string deviceCode, CancellationToken ct = default)
        => ReadAsync(d => d.DeviceCode == deviceCode, ct);

    Task<DeviceCodeRecord?> GetByUserCodeAsync(string userCode, CancellationToken ct = default)
        => ReadAsync(d => d.UserCode == userCode, ct);
}
