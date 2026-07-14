using Birko.Configuration;
using Birko.Data.Stores;

namespace Birko.Data.InMemory.Stores
{
    /// <summary>
    /// Synchronous in-memory data store. Stores all entities in a thread-safe dictionary
    /// for the lifetime of the instance. Settings are accepted for drop-in compatibility with
    /// other stores (so an in-memory store can stand in for a JSON / SQL store in tests) but
    /// are otherwise unused — there is no connection string or file path.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class InMemoryStore<T> : AbstractInMemoryStore<T>, ISettingsStore<Settings>, ISettingsStore<ISettings>
        where T : Models.AbstractModel
    {
        /// <summary>
        /// The (optional) settings for the store. Retained for compatibility; not used to back storage.
        /// </summary>
        protected Settings? _settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemoryStore{T}"/> class.
        /// </summary>
        public InMemoryStore()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InMemoryStore{T}"/> class with settings.
        /// </summary>
        /// <param name="settings">Optional settings (Location / Name); ignored for storage purposes.</param>
        public InMemoryStore(Settings settings)
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
        /// <remarks>
        /// CR-L127: only an <see cref="ISettings"/> that is the concrete <see cref="Settings"/> type is
        /// applied; any other implementation is a silent no-op. This is intentional and harmless — the
        /// InMemory store holds everything in a <c>ConcurrentDictionary</c> and never reads settings, so
        /// there is nothing to configure. Pass the concrete <see cref="Settings"/> if a value must stick.
        /// </remarks>
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
