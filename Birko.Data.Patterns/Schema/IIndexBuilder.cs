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

        /// <summary>
        /// Terminal operation that creates the accumulated index. Providers that create indexes
        /// eagerly keep the default no-op; SQL overrides this to emit CREATE INDEX. A migration must
        /// call <c>Build()</c> to finish a <c>CreateIndex(...).WithField(...)</c> chain — without it
        /// the SQL builder never executes (see CODE-REVIEW-AUDIT CR-C14).
        /// </summary>
        void Build() { }
    }
}
