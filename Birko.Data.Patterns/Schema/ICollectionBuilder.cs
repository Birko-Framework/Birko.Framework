namespace Birko.Data.Patterns.Schema
{
    public interface ICollectionBuilder
    {
        ICollectionBuilder WithField(string name, FieldType type,
            bool isPrimary = false, bool isUnique = false,
            bool isRequired = false, int? maxLength = null,
            int? precision = null, int? scale = null,
            bool isAutoIncrement = false, object? defaultValue = null);

        ICollectionBuilder WithField(FieldDescriptor field);

        /// <summary>
        /// Terminal operation that applies the accumulated collection definition. Providers that
        /// create collections eagerly (e.g. schemaless stores whose CreateCollection already ran)
        /// keep the default no-op; providers that must know all fields before creating (e.g. SQL,
        /// which emits a single CREATE TABLE) override this. A migration must call <c>Build()</c> to
        /// finish a <c>CreateCollection(...).WithField(...)</c> chain — without it the SQL builder
        /// never executes (see CODE-REVIEW-AUDIT CR-C14).
        /// </summary>
        void Build() { }
    }
}
