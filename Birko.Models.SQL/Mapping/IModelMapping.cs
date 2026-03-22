namespace Birko.Models.SQL.Mapping
{
    /// <summary>
    /// Marker interface for model mapping configurations.
    /// Implement this to define SQL mappings for a model type.
    /// </summary>
    public interface IModelMapping<T> where T : class
    {
        void Configure(ModelMap<T> map);
    }
}
