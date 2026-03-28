using System;

namespace Birko.Models.Pricing.ViewModels
{
    public class CurrencyRate : Data.ViewModels.LogViewModel
    {
        public string FromCurrencyCode { get; set; } = null!;
        public string ToCurrencyCode { get; set; } = null!;
        public decimal Rate { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        public int Source { get; set; }
    }
}
