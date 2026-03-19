using System;
using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Repositories
{
    /// <summary>
    /// Provides a centralized locator for managing repository instances with caching support.
    /// Thread-safe singleton pattern for repository creation and lifecycle management.
    /// </summary>
    public static class RepositoryLocator
    {
        #region Fields

        private static readonly object _lockObject = new();
        private static IDictionary<string, IDictionary<Type, object>>? _repositories;

        #endregion

        #region Repository Creation

        /// <summary>
        /// Get or create a repository with a specific store instance.
        /// </summary>
        /// <typeparam name="TStore">The type of store.</typeparam>
        /// <typeparam name="TRepository">The type of repository.</typeparam>
        /// <param name="store">The store instance to inject.</param>
        /// <param name="key">Optional key for caching multiple instances with different stores.</param>
        /// <returns>The repository instance.</returns>
        public static TRepository GetRepository<TStore, TRepository>(TStore store, string? key = null)
            where TStore : class, Stores.IBaseStore
            where TRepository : IBaseRepository
        {
            var id = key ?? typeof(TStore).FullName ?? string.Empty;
            var type = typeof(TRepository);
            lock (_lockObject)
            {
                _repositories ??= new Dictionary<string, IDictionary<Type, object>>();
                if (!_repositories.ContainsKey(id))
                {
                    _repositories.Add(id, new Dictionary<Type, object>());
                }

                if (!_repositories[id].ContainsKey(type))
                {
                    _repositories[id].Add(type, (TRepository)Activator.CreateInstance(type, store)!);
                }
            }
            return (TRepository)_repositories[id][type];
        }

        /// <summary>
        /// Get or create a repository with a specific store instance using a factory.
        /// </summary>
        /// <typeparam name="TRepository">The type of repository.</typeparam>
        /// <param name="storeFactory">Factory function to create the store.</param>
        /// <param name="key">Optional key for caching multiple instances.</param>
        /// <returns>The repository instance.</returns>
        public static TRepository GetRepository<TRepository>(Func<Stores.IBaseStore> storeFactory, string? key = null)
            where TRepository : IBaseRepository
        {
            var id = key ?? typeof(TRepository).FullName ?? string.Empty;
            var type = typeof(TRepository);
            lock (_lockObject)
            {
                _repositories ??= new Dictionary<string, IDictionary<Type, object>>();
                if (!_repositories.ContainsKey(id))
                {
                    _repositories.Add(id, new Dictionary<Type, object>());
                }

                if (!_repositories[id].ContainsKey(type))
                {
                    var store = storeFactory();
                    _repositories[id].Add(type, (TRepository)Activator.CreateInstance(type, store)!);
                }
            }
            return (TRepository)_repositories[id][type];
        }

        /// <summary>
        /// Get or create a repository using settings to determine the cache key.
        /// Creates the repository via default constructor.
        /// </summary>
        /// <typeparam name="TRepository">The type of repository.</typeparam>
        /// <typeparam name="TSettings">The type of settings.</typeparam>
        /// <param name="settings">The settings to use for cache key.</param>
        /// <returns>The repository instance.</returns>
        public static TRepository GetRepository<TRepository, TSettings>(TSettings settings)
            where TRepository : IBaseRepository
            where TSettings : Configuration.ISettings
        {
            var id = settings?.GetId() ?? string.Empty;
            var type = typeof(TRepository);
            lock (_lockObject)
            {
                _repositories ??= new Dictionary<string, IDictionary<Type, object>>();
                if (!_repositories.ContainsKey(id))
                {
                    _repositories.Add(id, new Dictionary<Type, object>());
                }

                if (!_repositories[id].ContainsKey(type))
                {
                    _repositories[id].Add(type, (TRepository)Activator.CreateInstance(type)!);
                }
            }
            return (TRepository)_repositories[id][type];
        }

        #endregion

        #region Repository Destruction

        /// <summary>
        /// Destroy a repository created with a specific store.
        /// </summary>
        /// <typeparam name="TRepository">The type of repository.</typeparam>
        /// <param name="key">The key used when creating the repository.</param>
        public static void Destroy<TRepository>(string? key = null)
            where TRepository : IBaseRepository
        {
            if (_repositories != null)
            {
                var id = key ?? typeof(TRepository).FullName ?? string.Empty;
                if (_repositories.ContainsKey(id))
                {
                    var type = typeof(TRepository);
                    if (_repositories[id].ContainsKey(type))
                    {
                        var repository = (TRepository)_repositories[id][type];
                        _repositories[id].Remove(type);
                        repository.Destroy();
                        if (!_repositories[id].Any())
                        {
                            _repositories.Remove(id);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Destroy a repository using settings to determine the cache key.
        /// </summary>
        /// <typeparam name="TRepository">The type of repository.</typeparam>
        /// <typeparam name="TSettings">The type of settings.</typeparam>
        /// <param name="settings">The settings used when creating the repository.</param>
        public static void Destroy<TRepository, TSettings>(TSettings settings)
            where TRepository : IBaseRepository
            where TSettings : Configuration.ISettings
        {
            Destroy<TRepository>(settings?.GetId());
        }

        #endregion
    }
}
