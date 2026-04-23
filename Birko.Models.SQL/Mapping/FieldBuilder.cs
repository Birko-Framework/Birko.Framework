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

        public FieldBuilder<T> HasPrecision(int precision)
        {
            _field.Precision = precision;
            return this;
        }

        public FieldBuilder<T> HasScale(int scale)
        {
            _field.Scale = scale;
            return this;
        }

        public FieldBuilder<T> HasMaxLength(int maxLength)
        {
            _field.MaxLength = maxLength;
            return this;
        }

        public FieldBuilder<T> HasIndex(string indexName, int order = 0, bool descending = false)
        {
            _field.IndexName = indexName;
            _field.IndexOrder = order;
            _field.IndexDescending = descending;
            return this;
        }

        public ModelMap<T> And() => _modelMap;
    }
}
