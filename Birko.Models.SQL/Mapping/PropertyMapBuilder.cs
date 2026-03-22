namespace Birko.Models.SQL.Mapping
{
    /// <summary>
    /// Fluent builder for property-level SQL mapping.
    /// </summary>
    public class PropertyMapBuilder<T> where T : class
    {
        private readonly ModelMap<T> _modelMap;
        private readonly PropertyMap _propertyMap;

        internal PropertyMapBuilder(ModelMap<T> modelMap, PropertyMap propertyMap)
        {
            _modelMap = modelMap;
            _propertyMap = propertyMap;
        }

        public PropertyMapBuilder<T> HasColumnName(string columnName)
        {
            _propertyMap.ColumnName = columnName;
            return this;
        }

        public PropertyMapBuilder<T> IsUnique()
        {
            _propertyMap.IsUnique = true;
            return this;
        }

        public PropertyMapBuilder<T> IsPrimary()
        {
            _propertyMap.IsPrimary = true;
            return this;
        }

        public PropertyMapBuilder<T> IsRequired()
        {
            _propertyMap.IsRequired = true;
            return this;
        }

        public PropertyMapBuilder<T> IsIncrement()
        {
            _propertyMap.IsIncrement = true;
            return this;
        }

        public PropertyMapBuilder<T> HasPrecision(int precision)
        {
            _propertyMap.Precision = precision;
            return this;
        }

        public PropertyMapBuilder<T> HasScale(int scale)
        {
            _propertyMap.Scale = scale;
            return this;
        }

        public PropertyMapBuilder<T> HasMaxLength(int maxLength)
        {
            _propertyMap.MaxLength = maxLength;
            return this;
        }

        public PropertyMapBuilder<T> HasIndex(string indexName, int order = 0, bool descending = false)
        {
            _propertyMap.IndexName = indexName;
            _propertyMap.IndexOrder = order;
            _propertyMap.IndexDescending = descending;
            return this;
        }

        /// <summary>
        /// Return to the model map for further configuration.
        /// </summary>
        public ModelMap<T> And() => _modelMap;
    }
}
