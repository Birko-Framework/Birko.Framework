using Birko.Configuration;
using Birko.Data.Stores;

namespace Birko.Data.InMemory.Stores
{
    /// <summary>
    /// Asynchronous in-memory data store. Stores all entities in a thread-safe dictionary
    /// for the lifetime of the instance. Settings are accepted for drop-in compatibility with
    /// other stores (so an in-memory store can stand in for a JSON / SQL store in tests) but
    /// are otherwise unused — there is no connection string or file path.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class AsyncInMemoryStore<T> : AbstractAsyncInMemoryStore<T>, ISettingsStore<Settings>, ISettingsStore<ISettings>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// The (optional) settings for the store. Retained for compatibility; not used to back storage.
        /// </summary>
        protected Settings? _settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncInMemoryStore{T}"/> class.
        /// </summary>
        public AsyncInMemoryStore()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncInMemoryStore{T}"/> class with settings.
        /// </summary>
        /// <param name="settings">Optional settings (Location / Name); ignored for storage purposes.</param>
        public AsyncInMemoryStore(Settings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Sets the store settings.
        /// </summary>
        /// <param name="settings">The settings to apply.</param>
        public virtual void SetSettings(Settings settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Sets the store settings using the <see cref="ISettings"/> interface.
        /// </summary>
        /// <param name="settings">The settings to apply.</param>
        public virtual void SetSettings(ISettings settings)
        {
            if (settings is Settings concrete)
            {
                SetSettings(concrete);
            }
        }
    }
}
