using System;

namespace Birko.Models.Pricing.ViewModels
{
    public class PriceListEntry : Data.ViewModels.LogViewModel
    {
        public Guid PriceListGuid { get; set; }
        public Guid ItemGuid { get; set; }
        public decimal? Price { get; set; }
        public decimal? PriceVAT { get; set; }
        public decimal? VAT { get; set; }
        public decimal? MinQuantity { get; set; }
    }
}
