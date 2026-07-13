namespace Birko.Data.Sync.Tests.TestInfrastructure;

/// <summary>
/// In-memory async bulk store for <see cref="TestSyncModel"/>, used as the local/remote endpoints
/// in the <see cref="AsyncSyncProvider{TStore,T,TKnowledge}"/> tests (the async counterpart to
/// <see cref="TestBulkStore"/>).
/// </summary>
public class TestAsyncBulkStore : InMemoryAsyncStore<TestSyncModel>
{
}
