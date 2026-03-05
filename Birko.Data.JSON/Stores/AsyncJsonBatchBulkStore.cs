using Birko.Data.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Async JSON bulk store that splits items into batch files (multiple items per file) with bulk operations support.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class AsyncJsonBatchBulkStore<T>
        : AsyncJsonSeparateBulkStore<T>
        where T : Models.AbstractModel
    {
        #region Fields and Properties

        /// <summary>
        /// The maximum number of items per batch file.
        /// </summary>
        private int _batchSize = 1024;

        #endregion

        #region Constructors and Initialization

        /// <summary>
        /// Initializes a new instance of the AsyncJsonBatchBulkStore class.
        /// </summary>
        public AsyncJsonBatchBulkStore() : base()
        {
        }

        /// <summary>
        /// Sets the batch settings for the store.
        /// </summary>
        /// <param name="settings">The batch settings to apply.</param>
        public virtual void SetSettings(BatchSettings settings)
        {
            _batchSize = settings.BatchSize;
            base.SetSettings(settings);
        }

        /// <summary>
        /// Sets the store settings.
        /// </summary>
        /// <param name="settings">The settings to apply.</param>
        public override void SetSettings(Settings settings)
        {
            SetSettings((BatchSettings)settings);
        }

        #endregion

        #region Data Persistence

        /// <inheritdoc />
        protected override async Task LoadDataAsync(CancellationToken ct)
        {
            var pathDirectory = PathDirectory;
            if (string.IsNullOrEmpty(pathDirectory) || !Directory.Exists(pathDirectory) || string.IsNullOrEmpty(_settings?.Name))
            {
                _items ??= new();
                return;
            }

            var files = Directory.GetFiles(pathDirectory, _settings.Name);
            if (files.Any())
            {
                _items = new();
                int batch = 1;
                foreach (var file in files)
                {
                    using var fileStream = new FileStream(
                        file,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read,
                        bufferSize: 4096,
                        useAsync: true);

                    var items = await ReadFromStreamAsync<IEnumerable<T>>(fileStream, ct);
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

                    byte[] bytes = new byte[16];
                    BitConverter.GetBytes(batch).CopyTo(bytes, 0);
                    AddFile(new Guid(bytes), file);
                    batch++;
                }
            }
        }

        /// <inheritdoc />
        protected override async Task SaveDataAsync(CancellationToken ct)
        {
            var pathDirectory = PathDirectory;
            if (string.IsNullOrEmpty(pathDirectory) || string.IsNullOrEmpty(_settings?.Name))
            {
                return;
            }

            var removedFiles = Directory.GetFiles(pathDirectory, _settings.Name).ToDictionary(x => x);

            int batch = 1;
            List<T> batchFiles = new();

            foreach (var item in _items)
            {
                batchFiles.Add(item.Value);
                if (batchFiles.Count == _batchSize)
                {
                    await SaveBatchAsync(batch, batchFiles, removedFiles, ct);
                    batch++;
                    batchFiles.Clear();
                }
            }

            if (batchFiles.Count != 0)
            {
                await SaveBatchAsync(batch, batchFiles, removedFiles, ct);
            }

            if (removedFiles.Count != 0)
            {
                foreach (var kvp in removedFiles)
                {
                    await Task.Run(() => File.Delete(kvp.Value), ct);
                }
            }
        }

        /// <summary>
        /// Saves a batch of items to a file asynchronously.
        /// </summary>
        /// <param name="batch">The batch number.</param>
        /// <param name="batchFiles">The items in the batch.</param>
        /// <param name="removedFiles">Dictionary of files to remove.</param>
        /// <param name="ct">Cancellation token.</param>
        private async Task SaveBatchAsync(int batch, List<T> batchFiles, Dictionary<string, string> removedFiles, CancellationToken ct)
        {
            var pathDirectory = PathDirectory;
            if (string.IsNullOrEmpty(pathDirectory))
            {
                return;
            }

            if (!Directory.Exists(pathDirectory))
            {
                Directory.CreateDirectory(pathDirectory);
            }

            byte[] bytes = new byte[16];
            BitConverter.GetBytes(batch).CopyTo(bytes, 0);
            var guid = new Guid(bytes);
            var fileName = _settings?.Name.Contains('*') ?? false
                ? _settings.Name.Replace("*", guid.ToString("D"))
                : $"{_settings?.Name}-{guid.ToString("D")}";

            // Validate the combined path even though fileName is constructed internally
            var path = PathValidator.CombineAndValidate(
                pathDirectory ?? throw new InvalidOperationException("PathDirectory cannot be null"),
                fileName);

            AddFile(new Guid(bytes), path);

            await Task.Run(() => File.Delete(path), ct);

            using var fileStream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                useAsync: true);

            await WriteToStreamAsync(fileStream, batchFiles, ct);

            if (removedFiles.ContainsKey(path))
            {
                removedFiles.Remove(path);
            }
        }

        #endregion
    }
}
