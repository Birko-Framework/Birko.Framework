using System;
using Birko.Data.Models;

namespace Birko.Models.Pricing
{
    /// <summary>
    /// Currency with exchange rates. Clean replacement for Accounting.Currency.
    /// </summary>
    public class Currency
        : AbstractLogModel
        , ILoadable<ViewModels.Currency>
        , ICopyable<Currency>
        , IDefault
    {
        public string Name { get; set; } = null!;
        public string Symbol { get; set; } = null!;
        public decimal FromRate { get; set; }
        public decimal ToRate { get; set; }
        public bool Default { get; set; }

        public virtual Currency CopyTo(Currency clone)
        {
            if (clone == null)
            {
                clone = new Currency();
            }
            base.CopyTo(clone);
            clone.Name = Name;
            clone.Symbol = Symbol;
            clone.FromRate = FromRate;
            clone.ToRate = ToRate;
            clone.Default = Default;
            return clone;
        }

        public virtual void LoadFrom(ViewModels.Currency data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Name = data.Name;
            Symbol = data.Symbol;
            FromRate = data.FromRate;
            ToRate = data.ToRate;
            Default = data.Default;
        }
    }
}
