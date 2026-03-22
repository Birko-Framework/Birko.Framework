namespace Birko.Models.SQL.Mapping
{
    /// <summary>
    /// SQL mapping metadata for a single property.
    /// </summary>
    public class PropertyMap
    {
        public string PropertyName { get; }
        public string? ColumnName { get; set; }
        public bool IsUnique { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsRequired { get; set; }
        public bool IsIgnored { get; set; }
        public bool IsIncrement { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public int? MaxLength { get; set; }
        public string? IndexName { get; set; }
        public int IndexOrder { get; set; }
        public bool IndexDescending { get; set; }

        public PropertyMap(string propertyName)
        {
            PropertyName = propertyName;
        }
    }
}
