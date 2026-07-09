using Birko.Data.Views;

namespace Birko.Data.ElasticSearch.Views;

/// <summary>
/// Single source of truth for the ElasticSearch index a view resolves to, shared by
/// <see cref="ElasticSearchViewStore{TView}"/> and <see cref="ElasticSearchViewManager"/>
/// (CR-M091: the store used <c>definition.Name</c> only in Persistent mode while the manager used it
/// whenever present, so for a named Auto-mode view the manager ensured/checked one index while the
/// store queried another).
/// </summary>
internal static class ElasticSearchViewIndexResolver
{
    /// <summary>
    /// For Persistent mode with a name, the view has its own destination index named after it.
    /// For OnTheFly/Auto, the view is computed over the primary source's own index.
    /// </summary>
    public static string Resolve(ViewDefinition definition)
    {
        if (definition.QueryMode == ViewQueryMode.Persistent && !string.IsNullOrEmpty(definition.Name))
        {
            return definition.Name!.ToLowerInvariant();
        }

        return definition.PrimarySource.Name.ToLowerInvariant();
    }
}
