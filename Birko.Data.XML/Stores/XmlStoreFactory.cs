using System;
using System.IO;
using Birko.Configuration;
using Birko.Data.Stores;

namespace Birko.Data.XML.Stores
{
    /// <summary>
    /// The XML-file <see cref="IStoreFactory"/> (TASK-990), chosen by the backend name <see cref="Name"/>. The settings'
    /// <c>Location</c> is a folder, and each type is an <see cref="AsyncXmlStore{T}"/> on its own file there, named by the
    /// type's full name (<c>MyApp.Models.Customer.xml</c>), so two types never share a file. One store per type, held by
    /// the factory: the store holds the file's contents, and two stores on one file would overwrite each other.
    /// </summary>
    public sealed class XmlStoreFactory : SettingsStoreFactory<Settings>
    {
        /// <summary>The backend name this factory registers under.</summary>
        public const string Name = "Xml";

        public XmlStoreFactory(Settings settings) : base(Name, settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Location))
                throw new ArgumentException($"Store backend '{Name}' needs a folder in Settings.Location.", nameof(settings));
            Directory.CreateDirectory(settings.Location);
        }

        /// <summary>
        /// A folder path (<c>data/xml</c>), or <c>Path=…</c>. Any other key is refused. The path is made absolute, so one
        /// folder is one set of files however it was written.
        /// </summary>
        public static Settings ParseConnectionString(string? connectionString)
            => new(FileStoreFolders.Parse(Name, connectionString), Name);

        /// <inheritdoc />
        protected override IAsyncBulkStore<T> NewStore<T>()
        {
            var store = new AsyncXmlStore<T>();
            store.SetSettings(new Settings(BackendSettings.Location, FileStoreFolders.FileName<T>(".xml")));
            return store;
        }
    }

    /// <summary>Registers XML files with <see cref="Birko.Data.Stores.StoreFactory"/> (TASK-990).</summary>
    public static class XmlStoreProviders
    {
        /// <summary>Adds the <c>Xml</c> backend: the connection string is a folder path or <c>Path=…</c>.</summary>
        public static StoreProviders AddXml(this StoreProviders providers)
            => providers.Add<Settings>(XmlStoreFactory.Name, XmlStoreFactory.ParseConnectionString, s => new XmlStoreFactory(s));
    }
}
