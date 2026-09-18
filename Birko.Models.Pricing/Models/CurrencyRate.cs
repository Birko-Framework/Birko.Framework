using System;
using Birko.Data.Models;

namespace Birko.Models.Pricing
{
    /// <summary>
    /// Exchange rate between two currencies, valid for a date range.
    /// </summary>
    public class CurrencyRate
        : AbstractLogModel
        , ILoadable<ViewModels.CurrencyRate>
        , ICopyable<CurrencyRate>
    {
        /// <summary>ISO 4217 code of the source currency.</summary>
        public string FromCurrencyCode { get; set; } = null!;
        /// <summary>ISO 4217 code of the target currency.</summary>
        public string ToCurrencyCode { get; set; } = null!;
        public decimal Rate { get; set; }
        public DateTime ValidFrom { get; set; }
        public DateTime? ValidTo { get; set; }
        /// <summary>Source of the rate: 0 = Manual, 1 = ECB, 2 = CNB.</summary>
        public int Source { get; set; }

        public virtual CurrencyRate CopyTo(CurrencyRate clone)
        {
            if (clone == null)
            {
                clone = new CurrencyRate();
            }
            base.CopyTo(clone);
            clone.FromCurrencyCode = FromCurrencyCode;
            clone.ToCurrencyCode = ToCurrencyCode;
            clone.Rate = Rate;
            clone.ValidFrom = ValidFrom;
            clone.ValidTo = ValidTo;
            clone.Source = Source;
            return clone;
        }

        public virtual void LoadFrom(ViewModels.CurrencyRate data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            FromCurrencyCode = data.FromCurrencyCode;
            ToCurrencyCode = data.ToCurrencyCode;
            Rate = data.Rate;
            ValidFrom = data.ValidFrom;
            ValidTo = data.ValidTo;
            Source = data.Source;
        }
    }
}
