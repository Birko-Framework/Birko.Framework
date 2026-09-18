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

        /// <summary>
        /// Hydrates the header fields from the view model. CR-L312: <see cref="Entries"/> is intentionally
        /// <b>not</b> mapped — the view model is header-only (it carries no Entries collection by design),
        /// so entries are loaded and persisted separately via the PriceListEntry store. A VM→model load
        /// therefore leaves <see cref="Entries"/> as its default empty collection rather than round-tripping
        /// children (same convention as InventoryDocument.Lines / CR-M221).
        /// </summary>
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
