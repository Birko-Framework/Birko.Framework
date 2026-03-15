using Birko.Data.Stores;

namespace Birko.Storage;

/// <summary>
/// Settings for storage providers. Extends Birko.Data.Stores.Settings.
/// Location = base directory (local) or endpoint (cloud). Name = logical storage name.
/// </summary>
public class StorageSettings : Settings, Birko.Data.Models.ILoadable<StorageSettings>
{
    /// <summary>
    /// Optional path prefix applied to all operations.
    /// Useful for tenant isolation (e.g., "tenant-123/").
    /// </summary>
    public string? PathPrefix { get; set; }

    /// <summary>
    /// Default options applied to all uploads when not overridden per-call.
    /// </summary>
    public StorageOptions? DefaultOptions { get; set; }

    public StorageSettings() : base() { }

    public StorageSettings(string location, string name, string? pathPrefix = null)
        : base(location, name)
    {
        PathPrefix = pathPrefix;
    }

    public override string GetId()
    {
        return string.IsNullOrEmpty(PathPrefix)
            ? base.GetId()
            : $"{base.GetId()}:{PathPrefix}";
    }

    public void LoadFrom(StorageSettings data)
    {
        base.LoadFrom(data);
        if (data != null)
        {
            PathPrefix = data.PathPrefix;
            DefaultOptions = data.DefaultOptions;
        }
    }

    public override void LoadFrom(Settings data)
    {
        if (data is StorageSettings storageData)
        {
            LoadFrom(storageData);
        }
    }
}
