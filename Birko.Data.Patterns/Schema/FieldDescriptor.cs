using System.Collections.Generic;
using System.Linq;

namespace Birko.Data.Patterns.Schema
{
    /// <summary>One index a column belongs to, as a fluent mapping declared it (TASK-695).</summary>
    public sealed class FieldIndex
    {
        public FieldIndex(string name, int order, bool descending, bool unique)
        {
            Name = name;
            Order = order;
            Descending = descending;
            Unique = unique;
        }

        public string Name { get; }
        public int Order { get; }
        public bool Descending { get; }
        /// <summary>The index is UNIQUE across all its columns (TASK-595).</summary>
        public bool Unique { get; }
    }

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

        /// <summary>
        /// Every index this column belongs to, in declaration order. A column may sit in several composite indexes
        /// (TASK-695); before, one set of index properties held a single index and a second declaration replaced it.
        /// </summary>
        public List<FieldIndex> Indexes { get; } = new List<FieldIndex>();

        /// <summary>The first of <see cref="Indexes"/>, or null.</summary>
        public string? IndexName => Indexes.FirstOrDefault()?.Name;
        /// <summary>The first index's column order.</summary>
        public int IndexOrder => Indexes.FirstOrDefault()?.Order ?? 0;
        /// <summary>The first index's direction.</summary>
        public bool IndexDescending => Indexes.FirstOrDefault()?.Descending ?? false;
        /// <summary>The first index is UNIQUE across all its columns (TASK-595).</summary>
        public bool IndexUnique => Indexes.FirstOrDefault()?.Unique ?? false;

        public FieldDescriptor() { }

        public FieldDescriptor(string name)
        {
            Name = name;
        }
    }
}
