using System;
using Birko.Configuration;
using Birko.Data.Stores;

namespace Birko.Data.InMemory.Stores
{
    /// <summary>
    /// The InMemory <see cref="IStoreFactory"/> (TASK-990), chosen by the backend name <see cref="Name"/>: the offline
    /// backend for tests and prototypes. One store per type, held by the factory, so every caller of a type sees the same
    /// data; nothing outlives the factory.
    /// </summary>
    public sealed class InMemoryStoreFactory : SettingsStoreFactory<Settings>
    {
        /// <summary>The backend name this factory registers under.</summary>
        public const string Name = "InMemory";

        public InMemoryStoreFactory() : this(new Settings(Name, Name)) { }

        public InMemoryStoreFactory(Settings settings) : base(Name, settings) { }

        /// <summary>InMemory takes no connection string: an empty one is accepted, anything else is refused.</summary>
        public static Settings ParseConnectionString(string? connectionString)
        {
            if (!string.IsNullOrWhiteSpace(connectionString))
                throw new ArgumentException($"Store backend '{Name}' takes no connection string; leave it empty.", nameof(connectionString));
            return new Settings(Name, Name);
        }

        /// <inheritdoc />
        protected override IAsyncBulkStore<T> NewStore<T>()
        {
            var store = new AsyncInMemoryStore<T>();
            store.SetSettings(BackendSettings);
            return store;
        }
    }

    /// <summary>Registers InMemory with <see cref="Birko.Data.Stores.StoreFactory"/> (TASK-990).</summary>
    public static class InMemoryStoreProviders
    {
        /// <summary>Adds the <c>InMemory</c> backend; its connection string is empty.</summary>
        public static StoreProviders AddInMemory(this StoreProviders providers)
            => providers.Add<Settings>(InMemoryStoreFactory.Name, InMemoryStoreFactory.ParseConnectionString, s => new InMemoryStoreFactory(s));
    }
}
