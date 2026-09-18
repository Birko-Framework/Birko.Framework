using System;
using Birko.Data.Models;
using Birko.Models.Contracts;

namespace Birko.Models.Pricing
{
    /// <summary>
    /// Single price entry within a price list. Links an item to its price.
    /// </summary>
    public class PriceListEntry
        : AbstractLogModel
        , IPriceable
        , ILoadable<ViewModels.PriceListEntry>
    {
        public Guid PriceListGuid { get; set; }
        public Guid ItemGuid { get; set; }
        public decimal? Price { get; set; }
        public decimal? PriceVAT { get; set; }
        public decimal? VAT { get; set; }
        public decimal? MinQuantity { get; set; }

        public virtual void LoadFrom(ViewModels.PriceListEntry data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            PriceListGuid = data.PriceListGuid;
            ItemGuid = data.ItemGuid;
            Price = data.Price;
            PriceVAT = data.PriceVAT;
            VAT = data.VAT;
            MinQuantity = data.MinQuantity;
        }
    }
}
