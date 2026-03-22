using System;
using Birko.Data.Models;

namespace Birko.Models.Pricing
{
    /// <summary>
    /// Customer price group with percentage modifier.
    /// Moved from Accounting to Pricing domain.
    /// </summary>
    public class PriceGroup
        : AbstractLogModel
        , ILoadable<ViewModels.PriceGroup>
        , ICopyable<PriceGroup>
        , IDefault
    {
        public string Name { get; set; } = null!;
        public decimal Percentage { get; set; }
        public bool Default { get; set; }

        public virtual PriceGroup CopyTo(PriceGroup clone)
        {
            if (clone == null)
            {
                clone = new PriceGroup();
            }
            base.CopyTo(clone);
            clone.Name = Name;
            clone.Percentage = Percentage;
            clone.Default = Default;
            return clone;
        }

        public virtual void LoadFrom(ViewModels.PriceGroup data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            Percentage = data.Percentage;
            Default = data.Default;
        }
    }
}
