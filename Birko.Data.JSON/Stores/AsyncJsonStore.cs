using Birko.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Birko.Data.Stores;
using Birko.Configuration;

namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// Async JSON file-based data store implementation.
    /// Stores all entities in a single JSON file.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class AsyncJsonStore<T> : AbstractAsyncJsonStore<T>, ISettingsStore<Settings>, ISettingsStore<ISettings>
        where T : Models.AbstractModel
    {
        #region Fields and Properties

        /// <summary>
        /// The settings for the JSON store.
        /// </summary>
        protected Settings? _settings = null;

        /// <summary>
        /// The settings this store was given, or a refusal naming the call that supplies them.
        /// </summary>
        /// <remarks>
        /// Nothing enforces that <c>SetSettings</c> was called: it is a plain assignment, and
        /// <c>InitCore</c> silently no-ops without settings, so lazy init reports success and the
        /// store then cannot produce a path to write to.
        /// </remarks>
        protected Settings RequireSettings()
            => _settings ?? throw new InvalidOperationException(
                $"{GetType().Name} has no settings, so it cannot write. Call "
                + "SetSettings(new Settings(location, name)) before using the store.");


        /// <inheritdoc />
        protected override void EnsureWritable() => RequireSettings();


        /// <summary>
        /// Gets the file path for the JSON store.
        /// </summary>
        public string? Path
        {
            get
            {
                return GetPath();
            }
        }

        /// <summary>
        /// Gets the directory path for the JSON store.
        /// </summary>
        public string? PathDirectory
        {
            get
            {
                return GetDirectory();
            }
        }

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AsyncJsonStore class.
        /// </summary>
        public AsyncJsonStore()
        {
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
        /// Sets the store settings using the ISettings interface.
        /// </summary>
        /// <param name="settings">The settings to apply.</param>
        public virtual void SetSettings(ISettings settings)
        {
            if (settings is Settings settings1)
            {
                SetSettings(settings1);
                return;
            }

            // Silently ignoring it left the store unconfigured, and an unconfigured store used to
            // accept writes and persist nothing (see EnsureWritable). So the caller believed both
            // that they had configured the store and that their writes had been saved.
            //
            // Measured before making this throw: Settings is the ONLY implementation of ISettings
            // across the framework and all consumer repos - every other match is a
            // `where TSettings : ISettings` constraint - so this refusal cannot fire for any type
            // that exists today. It is here for whoever writes the first one.
            throw new ArgumentException(
                $"Settings of type '{settings?.GetType().Name ?? "null"}' cannot configure "
                + $"{GetType().Name}; it needs a {nameof(Settings)} (or a subclass such as "
                + "SqLiteSettings). This used to be ignored, which left the store unconfigured.",
                nameof(settings));
        }

        /// <inheritdoc />
        protected override async Task InitCoreAsync(CancellationToken ct = default)
        {
            var path = Path;
            if (!string.IsNullOrEmpty(path) && !File.Exists(path) && (_settings is Settings settings))
            {
                try
                {
                    var directory = GetDirectory();
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    // Async file write
                    await File.WriteAllTextAsync(path, "[]", ct);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to initialize JSON store. Location: '{settings.Location}', Name: '{settings.Name}'. " +
                        $"See inner exception for details.",
                        ex);
                }
            }

            await EnsureDataLoadedAsync(ct);
        }

        /// <inheritdoc />
        public override async Task DestroyAsync(CancellationToken ct = default)
        {
            _items?.Clear();
            var path = Path;
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                await Task.Run(() => File.Delete(path), ct);
            }
        }

        #endregion

        #region Path Configuration

        /// <summary>
        /// Gets the file path for the JSON store.
        /// </summary>
        /// <returns>The file path, or null if settings are not configured.</returns>
        public virtual string? GetPath()
        {
            // CR-L132: guard both Location and Name, aligning with the sync JsonStore.GetPath (behavior
            // already converged because GetDirectory() rejects an empty Location, but the two should match).
            if (string.IsNullOrEmpty(_settings?.Location) || string.IsNullOrEmpty(_settings?.Name))
            {
                return null;
            }

            var directory = GetDirectory();
            if (string.IsNullOrEmpty(directory))
            {
                return null;
            }

            try
            {
                // Validate the path to prevent directory traversal attacks
                return PathValidator.CombineAndValidate(directory, _settings.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Invalid path configuration for store. Location: '{_settings.Location}', Name: '{_settings.Name}'. " +
                    $"See inner exception for details.",
                    ex);
            }
        }

        /// <summary>
        /// Gets the directory path for the JSON store.
        /// </summary>
        /// <returns>The directory path, or null if settings are not configured.</returns>
        public virtual string? GetDirectory()
        {
            if (string.IsNullOrEmpty(_settings?.Location))
            {
                return null;
            }

            try
            {
                // Validate the directory path
                return PathValidator.ValidateDirectory(_settings.Location);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Invalid directory configuration for store. Location: '{_settings.Location}'. " +
                    $"See inner exception for details.",
                    ex);
            }
        }

        #endregion

        #region Data Persistence

        /// <inheritdoc />
        protected override async Task LoadDataAsync(CancellationToken ct)
        {
            var path = Path;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                _items ??= new();
                return;
            }

            // Open file with async enabled
            using var fileStream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                useAsync: true);

            var items = await ReadFromStreamAsync<List<T>>(fileStream, ct);
            _items = new();
            if (items != null)
            {
                foreach (var item in items)
                {
                    if (item.Guid.HasValue)
                    {
                        _items.Add(item.Guid.Value, item);
                    }
                }
            }
        }

        /// <inheritdoc />
        protected override async Task SaveDataAsync(CancellationToken ct)
        {
            var path = Path;
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            // Delete and recreate file
            await Task.Run(() => File.Delete(path), ct);

            using var fileStream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await WriteToStreamAsync(fileStream, _items.Values, ct);
        }

        #endregion
    }
}
