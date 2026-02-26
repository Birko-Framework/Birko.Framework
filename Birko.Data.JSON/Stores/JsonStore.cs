using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Birko.Data.Helpers;

namespace Birko.Data.Stores
{
    public class JsonStore<T> 
        : AbstractJsonStore<T>
        , ISettingsStore<Settings>
        where T: Models.AbstractModel
    {

        protected Settings _settings = null;

        public string Path
        {
            get
            {
                return GetPath();
            }
        }

        public JsonStore(): base()
        {

        }

        public virtual void SetSettings(Settings settings)
        {
            _settings = settings;
            Init();
            LoadData();
        }

        public virtual string GetPath()
        {
            if (string.IsNullOrEmpty(_settings?.Location) || string.IsNullOrEmpty(_settings?.Name))
            {
                return null;
            }

            try
            {
                // Validate the path to prevent directory traversal attacks
                return PathValidator.CombineAndValidate(_settings.Location, _settings.Name);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"Invalid path configuration for store. Location: '{_settings.Location}', Name: '{_settings.Name}'. " +
                    $"See inner exception for details.",
                    ex);
            }
        }

        public override void Init()
        {
            if (!string.IsNullOrEmpty(Path) && !File.Exists(Path) && (_settings is Settings settings))
            {
                try
                {
                    // Validate the directory path before creating it
                    var validatedLocation = PathValidator.ValidateDirectory(settings.Location);

                    if (!Directory.Exists(validatedLocation))
                    {
                        Directory.CreateDirectory(validatedLocation);
                    }
                    File.WriteAllText(Path, "[]");
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"Failed to initialize JSON store. Location: '{settings.Location}', Name: '{settings.Name}'. " +
                        $"See inner exception for details.",
                        ex);
                }
            }
        }

        public override void Destroy()
        {
            _items?.Clear();
            if (!string.IsNullOrEmpty(Path) && File.Exists(Path))
            {
                File.Delete(Path);
            }
        }

        protected override void LoadData()
        {
            if (string.IsNullOrEmpty(Path) || !File.Exists(Path))
            {
                _items ??= new();
                return;
            }
            using FileStream fileStrem = File.OpenRead(Path);
            var items = ReadFromStream<List<T>>(fileStrem);
            _items = new();
            foreach (var item in items)
            {
                _items.Add(item.Guid.Value, item);
            }
        }

        protected override void SaveData()
        {
            if (string.IsNullOrEmpty(Path))
            {
                return;
            }
            File.Delete(Path);
            using FileStream fileStream = File.OpenWrite(Path);
            WriteToStream(fileStream, _items);
        }
    }
}
