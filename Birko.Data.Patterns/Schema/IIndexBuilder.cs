using Birko.Data.Patterns.IndexManagement;

namespace Birko.Data.Patterns.Schema
{
    public interface IIndexBuilder
    {
        IIndexBuilder WithField(string name, bool descending = false,
            IndexFieldType fieldType = IndexFieldType.Standard);
        IIndexBuilder Unique();
        IIndexBuilder Sparse();
        IIndexBuilder WithProperty(string key, object value);
    }
}
