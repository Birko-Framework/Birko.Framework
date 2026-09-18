using System;

namespace Birko.Models.Product.Filters
{
    public class UnitConversion
    {
        public Guid? ProductGuid { get; set; }
        public Guid? FromMeasureUnitGuid { get; set; }
        public Guid? ToMeasureUnitGuid { get; set; }
    }
}
