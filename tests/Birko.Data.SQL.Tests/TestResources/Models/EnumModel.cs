using Birko.Data.SQL.Attributes;
using Birko.Data.Models;

namespace Birko.Data.SQL.Tests.TestResources.Models
{
    /// <summary>
    /// Enum-carrying model for the IN/enum translation tests. Enums map to <c>IntegerField</c>
    /// (stored as INTEGER), so predicates over them must bind integral parameter values.
    /// </summary>
    public enum EnumModelState
    {
        Created = 0,
        Confirmed = 1,
        Paid = 2,
        Processing = 3,
        Shipped = 4,
        Cancelled = 5,
    }

    [Table("EnumModels")]
    public class EnumModel : AbstractLogModel
    {
        public string Name { get; set; } = null!;
        public EnumModelState State { get; set; }
        public EnumModelState? Fallback { get; set; }
    }
}
