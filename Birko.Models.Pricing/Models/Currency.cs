using System;
using Birko.Data.Models;

namespace Birko.Models.Pricing
{
    /// <summary>Mixin interface for entities that reference a Currency.</summary>
    public interface IRelatedToCurrency : Data.Models.ILoadable<ViewModels.Currency>
    {
        Guid? CurrencyGuid { get; set; }
        string CurrencySymbol { get; set; }
    }

    /// <summary>
    /// Currency definition with ISO code and symbol positioning.
    /// Exchange rates are handled separately by <see cref="CurrencyRate"/>.
    /// </summary>
    public class Currency
        : AbstractLogModel
        , ILoadable<ViewModels.Currency>
        , ICopyable<Currency>
        , IDefault
    {
        /// <summary>ISO 4217 code (e.g. EUR, USD, CZK).</summary>
        public string Code { get; set; } = null!;
        public string Name { get; set; } = null!;
        /// <summary>Currency symbol (e.g. €, $, Kč).</summary>
        public string Symbol { get; set; } = null!;
        /// <summary>True if symbol is placed before the amount ($100), false if after (100 €).</summary>
        public bool IsLeftSymbol { get; set; }
        public bool IsDefault { get; set; }

        public virtual Currency CopyTo(Currency clone)
        {
            if (clone == null)
            {
                clone = new Currency();
            }
            base.CopyTo(clone);
            clone.Code = Code;
            clone.Name = Name;
            clone.Symbol = Symbol;
            clone.IsLeftSymbol = IsLeftSymbol;
            clone.IsDefault = IsDefault;
            return clone;
        }

        public virtual void LoadFrom(ViewModels.Currency data)
        {
            base.LoadFrom(data);
            if (data == null) return;
            Code = data.Code;
            Name = data.Name;
            Symbol = data.Symbol;
            IsLeftSymbol = data.IsLeftSymbol;
            IsDefault = data.IsDefault;
        }
    }
}
