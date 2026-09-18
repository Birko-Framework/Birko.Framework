using Birko.Data.InMemory.Stores;
using Birko.Security.OAuth.Server.Models;
using Birko.Security.OAuth.Server.Stores;

namespace Birko.Security.OAuth.Server.Tests;

// Thread-safe in-memory stores for the test fixtures, backed by Birko.Data.InMemory's
// AsyncInMemoryStore<T>. Each marker store interface (IOAuthClientStore, etc.) adds only
// default-implemented lookup helpers that fall through to ReadAsync, so a bare subclass suffices.
public class InMemoryClientStore : AsyncInMemoryStore<OAuthClient>, IOAuthClientStore { }
public class InMemoryAuthorizationCodeStore : AsyncInMemoryStore<AuthorizationCode>, IAuthorizationCodeStore { }
public class InMemoryRefreshTokenStore : AsyncInMemoryStore<RefreshTokenRecord>, IRefreshTokenStore { }
public class InMemoryDeviceCodeStore : AsyncInMemoryStore<DeviceCodeRecord>, IDeviceCodeStore { }
public class InMemoryConsentStore : AsyncInMemoryStore<ConsentRecord>, IConsentStore { }
