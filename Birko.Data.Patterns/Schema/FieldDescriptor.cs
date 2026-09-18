namespace Birko.Data.Patterns.Schema
{
    public class FieldDescriptor
    {
        public string Name { get; set; } = null!;
        public FieldType Type { get; set; }
        public string? ColumnName { get; set; }
        public bool IsPrimary { get; set; }
        public bool IsUnique { get; set; }
        public bool IsRequired { get; set; }
        public bool IsIgnored { get; set; }
        public int? MaxLength { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        public bool IsAutoIncrement { get; set; }
        public object? DefaultValue { get; set; }
        public string? IndexName { get; set; }
        public int IndexOrder { get; set; }
        public bool IndexDescending { get; set; }

        public FieldDescriptor() { }

        public FieldDescriptor(string name)
        {
            Name = name;
        }
    }
}
