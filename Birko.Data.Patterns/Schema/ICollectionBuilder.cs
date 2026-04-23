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
    }
}
