using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Sync.Tests.TestInfrastructure;

/// <summary>
/// In-memory <see cref="IAsyncSyncKnowledgeItemStore{T}"/> for the AsyncSyncProvider tests.
/// Async CRUD is provided by <see cref="InMemoryAsyncStore{T}"/>; this class adds only the async
/// sync-bookkeeping members the async knowledge-store contract requires. Mirrors the sync
/// <see cref="TestSyncKnowledgeItemStore"/>.
/// </summary>
public class TestAsyncSyncKnowledgeItemStore : InMemoryAsyncStore<TestSyncKnowledge>, IAsyncSyncKnowledgeItemStore<TestSyncKnowledge>
{
    private readonly Dictionary<string, DateTime?> _syncTimes = new();

    public Task<DateTime?> GetLastSyncTimeAsync(string scope, CancellationToken cancellationToken)
    {
        return Task.FromResult(_syncTimes.GetValueOrDefault(scope));
    }

    public Task<DateTime?> SetLastSyncTimeAsync(string scope, DateTime? lastSyncTime, CancellationToken cancellationToken)
    {
        _syncTimes[scope] = lastSyncTime;
        return Task.FromResult(lastSyncTime);
    }

    public TestSyncKnowledge CreateKnowledgeItem(Guid guid, string? localItemHash, string? remoteItemHash, SyncOptions options)
    {
        return new TestSyncKnowledge
        {
            EntityGuid = guid,
            Scope = options.Scope,
            LastSyncedAt = DateTime.UtcNow,
            LocalVersion = localItemHash,
            RemoteVersion = remoteItemHash,
            IsLocalDeleted = string.IsNullOrEmpty(localItemHash),
            IsRemoteDeleted = string.IsNullOrEmpty(remoteItemHash)
        };
    }
}
