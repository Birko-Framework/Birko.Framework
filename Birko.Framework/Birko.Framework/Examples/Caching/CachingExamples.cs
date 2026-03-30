using System;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Memory;

namespace Birko.Framework.Examples.Caching
{
    /// <summary>
    /// Examples demonstrating the Birko.Caching framework.
    /// ICache provides a unified interface for memory, Redis, and hybrid cache backends.
    /// </summary>
    public static class CachingExamples
    {
        /// <summary>
        /// MemoryCache: in-memory ICache with background expired entry cleanup.
        /// </summary>
        public static async Task RunMemoryCacheExample()
        {
            ExampleOutput.WriteLine("=== Memory Cache Example ===\n");

            // MemoryCache with 30-second cleanup interval for expired entries
            using var cache = new MemoryCache(cleanupInterval: TimeSpan.FromSeconds(30));

            // Set a value with default options (5-minute absolute expiration)
            await cache.SetAsync("product:123", new { Name = "Wireless Mouse", Price = 29.99m });
            ExampleOutput.WriteLine("Set 'product:123' in cache");

            // Get a value - returns CacheResult<T> with HasValue and Value
            CacheResult<object> result = await cache.GetAsync<object>("product:123");
            ExampleOutput.WriteLine($"Get 'product:123' - HasValue: {result.HasValue}, Value: {result.Value}");

            // Check existence
            bool exists = await cache.ExistsAsync("product:123");
            ExampleOutput.WriteLine($"Exists 'product:123': {exists}");

            // Cache miss returns HasValue = false
            CacheResult<string> miss = await cache.GetAsync<string>("nonexistent");
            ExampleOutput.WriteLine($"Get 'nonexistent' - HasValue: {miss.HasValue}");

            // GetOrSet: returns cached value or creates it using the factory
            string description = await cache.GetOrSetAsync<string>(
                "product:123:desc",
                async ct =>
                {
                    ExampleOutput.WriteLine("  Factory called - computing value...");
                    return "Ergonomic wireless mouse with USB receiver";
                },
                CacheEntryOptions.Absolute(TimeSpan.FromMinutes(10)));
            ExampleOutput.WriteLine($"GetOrSet result: {description}");

            // Second call returns cached value without calling factory
            string cached = await cache.GetOrSetAsync<string>(
                "product:123:desc",
                async ct =>
                {
                    ExampleOutput.WriteLine("  Factory called again (should not appear)");
                    return "This should not be returned";
                });
            ExampleOutput.WriteLine($"GetOrSet cached: {cached}");

            // Remove a specific key
            await cache.RemoveAsync("product:123");
            exists = await cache.ExistsAsync("product:123");
            ExampleOutput.WriteLine($"\nAfter Remove - Exists 'product:123': {exists}");

            // Remove by prefix
            await cache.SetAsync("user:1:name", "Alice");
            await cache.SetAsync("user:1:email", "alice@example.com");
            await cache.SetAsync("user:2:name", "Bob");
            await cache.RemoveByPrefixAsync("user:1:");
            ExampleOutput.WriteLine($"After RemoveByPrefix 'user:1:' - user:1:name exists: {await cache.ExistsAsync("user:1:name")}");
            ExampleOutput.WriteLine($"  user:2:name exists: {await cache.ExistsAsync("user:2:name")}");

            // Clear all entries
            await cache.ClearAsync();
            ExampleOutput.WriteLine($"After ClearAsync - user:2:name exists: {await cache.ExistsAsync("user:2:name")}");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// CacheEntryOptions: absolute and sliding expiration.
        /// </summary>
        public static async Task RunCacheExpirationExample()
        {
            ExampleOutput.WriteLine("=== Cache Expiration Example ===\n");

            using var cache = new MemoryCache();

            // Absolute expiration: entry expires after fixed duration from creation
            var absoluteOptions = CacheEntryOptions.Absolute(TimeSpan.FromMinutes(30));
            await cache.SetAsync("session:abc", "user-data", absoluteOptions);
            ExampleOutput.WriteLine("Absolute expiration (30 min): entry expires 30 min after creation, regardless of access");

            // Sliding expiration: entry expires if not accessed within duration
            var slidingOptions = CacheEntryOptions.Sliding(TimeSpan.FromMinutes(5));
            await cache.SetAsync("activity:user1", "last-seen", slidingOptions);
            ExampleOutput.WriteLine("Sliding expiration (5 min): resets on each access, expires if idle 5 min");

            // Combined: sliding expiration with an absolute cap
            var combinedOptions = CacheEntryOptions.AbsoluteAndSliding(
                ttl: TimeSpan.FromHours(1),
                sliding: TimeSpan.FromMinutes(10));
            await cache.SetAsync("token:xyz", "refresh-token", combinedOptions);
            ExampleOutput.WriteLine("Combined (1h absolute + 10min sliding): slides up to 1h max lifetime");

            // Custom options with priority
            var highPriority = new CacheEntryOptions
            {
                AbsoluteExpiration = TimeSpan.FromHours(24),
                Priority = CachePriority.High
            };
            await cache.SetAsync("config:app", "settings-data", highPriority);
            ExampleOutput.WriteLine("High priority (24h): less likely to be evicted under memory pressure");

            // NeverRemove priority
            var permanent = new CacheEntryOptions
            {
                Priority = CachePriority.NeverRemove
            };
            await cache.SetAsync("static:constants", "app-constants", permanent);
            ExampleOutput.WriteLine("NeverRemove: not evicted by cleanup timer (no expiration set)");

            // Default options: 5-minute absolute expiration
            ExampleOutput.WriteLine($"\nCacheEntryOptions.Default: {CacheEntryOptions.Default.AbsoluteExpiration?.TotalMinutes} min absolute");

            ExampleOutput.WriteLine("\nCachePriority levels:");
            ExampleOutput.WriteLine("  Low         - first to be evicted");
            ExampleOutput.WriteLine("  Normal      - default eviction priority");
            ExampleOutput.WriteLine("  High        - evicted only under pressure");
            ExampleOutput.WriteLine("  NeverRemove - not evicted by cleanup timer");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }

        /// <summary>
        /// RedisCache: distributed cache using StackExchange.Redis.
        /// </summary>
        public static async Task RunRedisCacheExample()
        {
            ExampleOutput.WriteLine("=== Redis Cache Example ===\n");

            // RedisCache requires StackExchange.Redis NuGet package
            // and a running Redis server.

            ExampleOutput.WriteLine("RedisCacheOptions configuration:");
            ExampleOutput.WriteLine("  ConnectionString  - Redis server (e.g., \"localhost:6379\")");
            ExampleOutput.WriteLine("  InstanceName      - key prefix for multi-app isolation");
            ExampleOutput.WriteLine("  DefaultExpiration - fallback TTL (default: 5 min)");
            ExampleOutput.WriteLine("  Database          - Redis database index 0-15 (default: 0)\n");

            // var options = new Birko.Caching.Redis.RedisCacheOptions
            // {
            //     ConnectionString = "localhost:6379",
            //     InstanceName = "myapp",
            //     DefaultExpiration = TimeSpan.FromMinutes(10),
            //     Database = 0
            // };

            try
            {
                // RedisCache implements the same ICache interface as MemoryCache
                var settings = new Birko.Redis.RedisSettings
                {
                    Location = "localhost",
                    Port = 6379,
                    KeyPrefix = "birko-example"
                };

                using var cache = new Birko.Caching.Redis.RedisCache(settings, TimeSpan.FromMinutes(5));

                // Same API as MemoryCache:
                // await cache.SetAsync("key", value, CacheEntryOptions.Absolute(TimeSpan.FromMinutes(5)));
                // var result = await cache.GetAsync<string>("key");
                // await cache.GetOrSetAsync("key", factory, options);

                ExampleOutput.WriteLine("RedisCache created (connection is lazy - connects on first use).");
                ExampleOutput.WriteLine("RedisConnectionManager provides:");
                ExampleOutput.WriteLine("  GetDatabase() - IDatabase for Redis operations");
                ExampleOutput.WriteLine("  GetServer()   - IServer for admin operations (KEYS, FLUSHDB)");
                ExampleOutput.WriteLine("  IsConnected   - connection status check");

                ExampleOutput.WriteLine("\nRedis features beyond MemoryCache:");
                ExampleOutput.WriteLine("  - Distributed: shared across multiple app instances");
                ExampleOutput.WriteLine("  - GetOrSetAsync uses Redis SET NX as distributed lock");
                ExampleOutput.WriteLine("  - Sliding expiration metadata stored in Redis hashes");
                ExampleOutput.WriteLine("  - RemoveByPrefixAsync uses server KEYS scan");
                ExampleOutput.WriteLine("  - ClearAsync flushes by prefix or entire database");
            }
            catch (Exception ex)
            {
                ExampleOutput.WriteLine($"Redis connection failed (expected without running Redis): {ex.Message}");
            }

            ExampleOutput.WriteLine("\nICache interface (shared by MemoryCache and RedisCache):");
            ExampleOutput.WriteLine("  GetAsync<T>(key)                         - lookup");
            ExampleOutput.WriteLine("  SetAsync<T>(key, value, options?)        - store");
            ExampleOutput.WriteLine("  RemoveAsync(key)                         - delete");
            ExampleOutput.WriteLine("  ExistsAsync(key)                         - check");
            ExampleOutput.WriteLine("  GetOrSetAsync<T>(key, factory, options?) - get-or-create");
            ExampleOutput.WriteLine("  RemoveByPrefixAsync(prefix)              - bulk delete");
            ExampleOutput.WriteLine("  ClearAsync()                             - remove all");

            ExampleOutput.WriteLine("\n=== Example Complete ===");
        }
    }
}
