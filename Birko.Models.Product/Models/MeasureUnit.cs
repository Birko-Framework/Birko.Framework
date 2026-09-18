using System;
using Birko.Data.Models;
using Birko.Models.Contracts;

namespace Birko.Models.Product
{
    public interface IRelatedToMeasureUnit : Birko.Data.Models.ILoadable<ViewModels.MeasureUnit>
    {
        Guid? MeasureUnitGuid { get; set; }
    }

    /// <summary>
    /// Unit of measure codebook (ks, kg, l, bal, kartón...).
    /// </summary>
    public class MeasureUnit
        : AbstractLogModel
        , ILoadable<ViewModels.MeasureUnit>
        , IDefault
        , ISortable
    {
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Symbol { get; set; }
        public bool IsDefault { get; set; }
        public int SortOrder { get; set; }

        public virtual void LoadFrom(ViewModels.MeasureUnit data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Code = data.Code;
            Name = data.Name;
            Symbol = data.Symbol;
            IsDefault = data.IsDefault;
            SortOrder = data.SortOrder;
        }
    }
}
