using Birko.Data.InMemory.Stores;
using Birko.Data.Sync.Models;

namespace Birko.Data.Sync.Tests.TestInfrastructure;

/// <summary>
/// In-memory bulk store for <see cref="TestSyncModel"/>, used as local/remote endpoints in the
/// sync provider tests. Backed by Birko.Data.InMemory's <see cref="InMemoryStore{T}"/>.
/// </summary>
public class TestBulkStore : InMemoryStore<TestSyncModel>
{
}
