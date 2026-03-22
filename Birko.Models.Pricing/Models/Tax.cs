using System;
using Birko.Data.Models;

namespace Birko.Models.Pricing
{
    /// <summary>
    /// Tax rate definition. Clean replacement for Accounting.Tax.
    /// </summary>
    public class Tax
        : AbstractLogModel
        , ILoadable<ViewModels.Tax>
        , ICopyable<Tax>
        , IDefault
    {
        public string Name { get; set; } = null!;
        public string ShortCut { get; set; } = null!;
        public decimal Percentage { get; set; }
        public bool Default { get; set; }

        public virtual Tax CopyTo(Tax clone)
        {
            if (clone == null)
            {
                clone = new Tax();
            }
            base.CopyTo(clone);
            clone.Name = Name;
            clone.ShortCut = ShortCut;
            clone.Percentage = Percentage;
            clone.Default = Default;
            return clone;
        }

        public virtual void LoadFrom(ViewModels.Tax data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            ShortCut = data.ShortCut;
            Percentage = data.Percentage;
            Default = data.Default;
        }
    }
}
