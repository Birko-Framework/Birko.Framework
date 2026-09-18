using System;
using Birko.Configuration;
using Birko.Data.Models;
using Raven.Client.Documents;
using Raven.Client.Documents.Conventions;

namespace Birko.Data.RavenDB.Stores
{
    /// <summary>
    /// RavenDB-specific settings for database connection.
    /// Extends RemoteSettings with RavenDB-specific configuration.
    /// RemoteSettings.Location = server URL, Name = database name.
    /// </summary>
    public class Settings : RemoteSettings, ILoadable<Settings>
    {
        /// <summary>
        /// Gets or sets the request timeout for RavenDB operations. Default is 30 seconds.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);

        public Settings() : base() { }

        public Settings(string location, string name, string? username = null, string? password = null)
            : base(location, name, username ?? string.Empty, password ?? string.Empty, 0, false) { }

        /// <summary>
        /// Creates and initializes a RavenDB DocumentStore from the current settings.
        /// </summary>
        public virtual IDocumentStore CreateDocumentStore()
        {
            var store = new DocumentStore
            {
                Urls = new[] { Location },
                Database = Name,
                Conventions = new DocumentConventions
                {
                    RequestTimeout = RequestTimeout
                }
            };
            store.Initialize();
            return store;
        }

        public override string GetId()
        {
            return $"{Location}:{Name}";
        }

        public void LoadFrom(Settings data)
        {
            if (data != null)
            {
                base.LoadFrom((RemoteSettings)data);
                RequestTimeout = data.RequestTimeout;
            }
        }

        public override void LoadFrom(Birko.Configuration.Settings data)
        {
            if (data is Settings ravenData)
            {
                LoadFrom(ravenData);
            }
            else
            {
                base.LoadFrom(data);
            }
        }
    }
}
