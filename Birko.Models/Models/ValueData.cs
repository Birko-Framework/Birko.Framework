using System;
using Birko.Data.Models;
using Birko.Data.ViewModels;

namespace Birko.Models
{
    public interface IValueData
        : ILoadable<ViewModels.Value>
        , ILoadable<IValueData>
        , ICopyable<ValueData>
    {
        decimal? Price { get; set; }
        decimal? PriceVAT { get; set; }
        decimal? VAT { get; set; }
    }

    public class ValueData
        : AbstractLogModel
        , IValueData
        , ILoadable<ValueData>
        , Birko.Models.Contracts.IPriceable
    {
        public const int StoreDecimalPlaces = 6;
        public const int StoreDecimalPrecision = 22;

        public virtual decimal? Price { get; set; }

        public virtual decimal? PriceVAT { get; set; }

        public virtual decimal? VAT { get; set; }

        public virtual ValueData CopyTo(ValueData clone)
        {
            if (clone == null)
            {
                clone = new ValueData();
            }
            base.CopyTo(clone);
            clone.Price = Price;
            clone.PriceVAT = PriceVAT;
            clone.VAT = VAT;

            return clone;
        }

        public virtual void LoadFrom(IValueData data)
        {
            if (data == null) return;

            Price = data.Price != null ? Math.Round(data.Price.Value, StoreDecimalPlaces) : (decimal?)null;
            PriceVAT = data.PriceVAT != null ? Math.Round(data.PriceVAT.Value, StoreDecimalPlaces) : (decimal?)null;
            VAT = data.VAT != null ? Math.Round(data.VAT.Value, StoreDecimalPlaces) : (decimal?)null;
        }

        public virtual void LoadFrom(ValueData data)
        {
            if (data != null)
            {
                LoadFrom((IValueData)data);
            }
        }

        public virtual void LoadFrom(ViewModels.Value data)
        {
            if (data == null) return;

            Price = data.Price != null ? Math.Round(data.Price.Value, StoreDecimalPlaces) : (decimal?)null;
            PriceVAT = data.PriceVAT != null ? Math.Round(data.PriceVAT.Value, StoreDecimalPlaces) : (decimal?)null;
            VAT = data.VAT != null ? Math.Round(data.VAT.Value, StoreDecimalPlaces) : (decimal?)null;
        }
    }
}
