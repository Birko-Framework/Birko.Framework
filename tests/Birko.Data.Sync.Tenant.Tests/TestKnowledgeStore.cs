using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;

namespace Birko.Data.Sync.Tenant.Tests;

/// <summary>A knowledge row for the tests: the engine's own contract, nothing tenant-specific.</summary>
public sealed class Knowledge : AbstractModel, ISyncKnowledgeItem
{
    public Guid EntityGuid { get; set; }
    public string Scope { get; set; } = string.Empty;
    public DateTime LastSyncedAt { get; set; }
    public string? LocalVersion { get; set; }
    public string? RemoteVersion { get; set; }
    public bool IsLocalDeleted { get; set; }
    public bool IsRemoteDeleted { get; set; }
    public string? Metadata { get; set; }
}

/// <summary>
/// An in-memory knowledge store (TASK-630: the tenant provider takes the engine's knowledge store). It records which
/// scopes were read and stamped, so "knowledge is keyed to the tenant the writes were scoped to" is asserted on the run
/// itself. <paramref name="lastSyncTime"/> seeds every scope as already synced (not initial).
/// </summary>
public sealed class TestKnowledgeStore : AsyncInMemoryStore<Knowledge>, IAsyncSyncKnowledgeItemStore<Knowledge>
{
    private readonly DateTime? _seededTime;
    private readonly Dictionary<string, DateTime?> _times = new();

    public TestKnowledgeStore(DateTime? lastSyncTime = null, IEnumerable<Knowledge>? existing = null)
    {
        _seededTime = lastSyncTime;
        foreach (var row in existing ?? Enumerable.Empty<Knowledge>())
        {
            CreateAsync(row).GetAwaiter().GetResult();
        }
    }

    public List<string> ReadScopes { get; } = new();
    public List<string> StampedScopes { get; } = new();

    public Task<DateTime?> GetLastSyncTimeAsync(string scope, CancellationToken cancellationToken)
    {
        ReadScopes.Add(scope);
        return Task.FromResult(_times.TryGetValue(scope, out var time) ? time : _seededTime);
    }

    public Task<DateTime?> SetLastSyncTimeAsync(string scope, DateTime? lastSyncTime, CancellationToken cancellationToken)
    {
        StampedScopes.Add(scope);
        _times[scope] = lastSyncTime;
        return Task.FromResult(lastSyncTime);
    }

    public Knowledge CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options) => new()
    {
        EntityGuid = guid,
        Scope = options.Scope,
        LastSyncedAt = DateTime.UtcNow,
        LocalVersion = localItemHash,
        RemoteVersion = remoteItemHash,
        IsLocalDeleted = string.IsNullOrEmpty(localItemHash),
        IsRemoteDeleted = string.IsNullOrEmpty(remoteItemHash)
    };

    /// <summary>The entity rows (not the engine's scope marker).</summary>
    public List<Knowledge> EntityRows()
        => ReadAsync(k => k.EntityGuid != Guid.Empty, null, null, null).GetAwaiter().GetResult().ToList();
}
