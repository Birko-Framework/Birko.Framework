using System;
using System.Collections.Generic;
using Birko.Data.Models;

namespace Birko.Models.Pricing
{
    /// <summary>
    /// Named price list containing price entries for items.
    /// </summary>
    public class PriceList
        : AbstractLogModel
        , ILoadable<ViewModels.PriceList>
    {
        public string Name { get; set; } = null!;
        public Guid? CurrencyGuid { get; set; }
        public DateTime? ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public bool IsActive { get; set; } = true;
        public ICollection<PriceListEntry> Entries { get; set; } = new List<PriceListEntry>();

        public virtual void LoadFrom(ViewModels.PriceList data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            CurrencyGuid = data.CurrencyGuid;
            ValidFrom = data.ValidFrom;
            ValidTo = data.ValidTo;
            IsActive = data.IsActive;
        }
    }
}
