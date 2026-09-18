using Birko.Data.InMemory.Stores;
using Birko.Data.Sync.Models;
using Birko.Data.Sync.Stores;
using System;
using System.Collections.Generic;

namespace Birko.Data.Sync.Tests.TestInfrastructure;

/// <summary>
/// In-memory <see cref="ISyncKnowledgeItemStore{T}"/> for the sync provider tests.
/// CRUD is provided by Birko.Data.InMemory's <see cref="InMemoryStore{T}"/>; this class adds
/// only the sync-bookkeeping members the knowledge-store contract requires.
/// </summary>
public class TestSyncKnowledgeItemStore : InMemoryStore<TestSyncKnowledge>, ISyncKnowledgeItemStore<TestSyncKnowledge>
{
    private readonly Dictionary<string, DateTime?> _syncTimes = new();

    public DateTime? GetLastSyncTime(string scope)
    {
        return _syncTimes.GetValueOrDefault(scope);
    }

    public DateTime? SetLastSyncTime(string scope, DateTime? lastSyncTime)
    {
        _syncTimes[scope] = lastSyncTime;
        return lastSyncTime;
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
