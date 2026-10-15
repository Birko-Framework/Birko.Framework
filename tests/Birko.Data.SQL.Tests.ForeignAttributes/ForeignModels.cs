using System;
using Birko.Data.SQL.Attributes;

namespace Birko.Data.SQL.Tests.ForeignAttributes
{
    /// <summary>
    /// TASK-577. One property per attribute type in <c>Birko.Data.SQL.Attributes</c>, each carrying values that differ
    /// from the type's defaults, so a rebuild that drops or zeroes a value is visible. Every attribute here is of this
    /// assembly's identity, not the one <c>Birko.Data.SQL</c> was compiled with.
    /// </summary>
    [Table("ForeignAttributed")]
    [CompositeIndex("UX_Foreign_Pair", nameof(Sequence), nameof(Amount), IsUnique = true)]
    public class ForeignModel
    {
        [PrimaryField, RequiredField]
        public Guid Id { get; set; }

        [UniqueField]
        public string? Email { get; set; }

        [IncrementField]
        public int Sequence { get; set; }

        [MaxLengthField(64)]
        public string? Name { get; set; }

        [PrecisionField(18), ScaleField(4)]
        public decimal Amount { get; set; }

        [UtcField]
        public DateTime At { get; set; }

        [NamedField("renamed_column")]
        public int Renamed { get; set; }

        [IndexedField("UX_Foreign_Code", 1, true, true, WhereNotNull = new[] { nameof(Code) })]
        public string? Code { get; set; }

        [IgnoreField]
        public string? NotStored { get; set; }
    }
}

namespace Birko.Data.SQL.Attributes
{
    /// <summary>
    /// Stands in for a field attribute added to <c>Birko.Data.SQL.Attributes</c> after this build: same namespace,
    /// a type name the rebuild does not know.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property)]
    public sealed class NewerField : Field
    {
    }
}

namespace Birko.Data.SQL.Tests.ForeignAttributes
{
    public class NewerModel
    {
        [Birko.Data.SQL.Attributes.NewerField]
        public int Value { get; set; }
    }
}
