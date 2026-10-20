using Birko.Data.Patterns.Schema;

namespace Birko.Models.SQL.Mapping
{
    public class FieldBuilder<T> where T : class
    {
        private readonly ModelMap<T> _modelMap;
        private readonly FieldDescriptor _field;

        internal FieldBuilder(ModelMap<T> modelMap, FieldDescriptor field)
        {
            _modelMap = modelMap;
            _field = field;
        }

        public FieldBuilder<T> HasColumnName(string columnName)
        {
            _field.ColumnName = columnName;
            return this;
        }

        public FieldBuilder<T> IsUnique()
        {
            _field.IsUnique = true;
            return this;
        }

        public FieldBuilder<T> IsPrimary()
        {
            _field.IsPrimary = true;
            return this;
        }

        public FieldBuilder<T> IsRequired()
        {
            _field.IsRequired = true;
            return this;
        }

        public FieldBuilder<T> IsAutoIncrement()
        {
            _field.IsAutoIncrement = true;
            return this;
        }

        public FieldBuilder<T> IsIgnored()
        {
            _field.IsIgnored = true;
            return this;
        }

        /// <summary>Mapping metadata only — NOT applied by <c>ModelMapRegistry.ApplyToDatabase()</c>
        /// (declare precision via the model's SQL field attributes). Readable via GetPropertyMaps.</summary>
        public FieldBuilder<T> HasPrecision(int precision)
        {
            _field.Precision = precision;
            return this;
        }

        /// <summary>Mapping metadata only — NOT applied by <c>ModelMapRegistry.ApplyToDatabase()</c>
        /// (declare scale via the model's SQL field attributes). Readable via GetPropertyMaps.</summary>
        public FieldBuilder<T> HasScale(int scale)
        {
            _field.Scale = scale;
            return this;
        }

        /// <summary>
        /// Bounds a <c>string</c> (or <c>byte[]</c>) column to <paramref name="maxLength"/>, as <c>[MaxLengthField]</c> does
        /// and winning over one; applied by <c>ModelMapRegistry.ApplyToDatabase()</c> (TASK-691), which refuses it on any
        /// other type.
        /// </summary>
        public FieldBuilder<T> HasMaxLength(int maxLength)
        {
            _field.MaxLength = maxLength;
            return this;
        }

        /// <summary>
        /// Adds this column to the index <paramref name="indexName"/> at <paramref name="order"/>. Columns sharing a
        /// name form one composite index. <c>ModelMapRegistry.ApplyToDatabase()</c> writes it into the table's index
        /// declarations, so the store's schema-ensure creates it (TASK-595; before that it was metadata nothing read).
        /// </summary>
        public FieldBuilder<T> HasIndex(string indexName, int order = 0, bool descending = false)
        {
            _field.IndexName = indexName;
            _field.IndexOrder = order;
            _field.IndexDescending = descending;
            return this;
        }

        /// <summary>
        /// Like <see cref="HasIndex"/>, but the index is UNIQUE across every column that names it, which is how a
        /// composite natural key such as (Provider, ProviderKey) is declared. <see cref="IsUnique"/> is single-column.
        /// Every column of one index must agree: mixing <c>HasIndex</c> and <c>HasUniqueIndex</c> under one name is
        /// refused by <c>ApplyToDatabase</c>.
        /// </summary>
        public FieldBuilder<T> HasUniqueIndex(string indexName, int order = 0, bool descending = false)
        {
            HasIndex(indexName, order, descending);
            _field.IndexUnique = true;
            return this;
        }

        public ModelMap<T> And() => _modelMap;
    }
}
