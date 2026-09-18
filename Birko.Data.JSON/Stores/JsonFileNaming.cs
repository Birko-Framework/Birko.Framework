namespace Birko.Data.JSON.Stores
{
    /// <summary>
    /// Shared file-naming rules for the separate/batch JSON stores. A record is saved as
    /// <c>{Name}-{key}</c> (or <c>Name.Replace("*", key)</c> when Name contains a wildcard), so the
    /// reload/enumerate search pattern must be built the same way — using the bare Name as the glob
    /// never matched the <c>-{key}</c> suffix, so previously-saved entities were never reloaded
    /// after a fresh process start (CR-H051).
    /// </summary>
    internal static class JsonFileNaming
    {
        public static string SearchPattern(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "*";
            }
            return name.Contains('*') ? name : $"{name}-*";
        }
    }
}
