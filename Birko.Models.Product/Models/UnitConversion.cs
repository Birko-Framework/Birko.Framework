using System;
using Birko.Data.Models;

namespace Birko.Models.Product
{
    /// <summary>
    /// Conversion factor between two units of measure.
    /// When ProductGuid is null the conversion is global; otherwise product-specific.
    /// </summary>
    public class UnitConversion
        : AbstractLogModel
        , ILoadable<ViewModels.UnitConversion>
    {
        public Guid FromMeasureUnitGuid { get; set; }
        public Guid ToMeasureUnitGuid { get; set; }
        public decimal Factor { get; set; }
        public Guid? ProductGuid { get; set; }

        public virtual void LoadFrom(ViewModels.UnitConversion data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            FromMeasureUnitGuid = data.FromMeasureUnitGuid;
            ToMeasureUnitGuid = data.ToMeasureUnitGuid;
            Factor = data.Factor;
            ProductGuid = data.ProductGuid;
        }
    }
}
