namespace Birko.Data.Views;

/// <summary>
/// Implement to define a view mapping configuration.
/// Follows the IModelMapping{T} pattern from Birko.Models.SQL.
/// </summary>
public interface IViewMapping<TView> where TView : class
{
    void Configure(ViewDefinitionBuilder<TView> builder);
}
