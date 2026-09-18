using System;

namespace Birko.Models.ValueObjects
{
    /// <summary>
    /// Immutable value object representing price with VAT breakdown.
    /// Replaces ValueData's scattered Price/PriceVAT/VAT pattern.
    /// </summary>
    public sealed class MoneyWithTax : IEquatable<MoneyWithTax>
    {
        public decimal? Price { get; }
        public decimal? PriceVAT { get; }
        public decimal? VAT { get; }

        public MoneyWithTax(decimal? price, decimal? priceVAT, decimal? vat)
        {
            Price = price;
            PriceVAT = priceVAT;
            VAT = vat;
        }

        public static MoneyWithTax Empty => new MoneyWithTax(null, null, null);

        public static MoneyWithTax FromNetAndVat(decimal net, decimal vat)
            => new MoneyWithTax(net, net + vat, vat);

        public static MoneyWithTax FromGrossAndVat(decimal gross, decimal vat)
            => new MoneyWithTax(gross - vat, gross, vat);

        public MoneyWithTax Round(int decimals) => new MoneyWithTax(
            Price.HasValue ? Math.Round(Price.Value, decimals) : (decimal?)null,
            PriceVAT.HasValue ? Math.Round(PriceVAT.Value, decimals) : (decimal?)null,
            VAT.HasValue ? Math.Round(VAT.Value, decimals) : (decimal?)null
        );

        public bool Equals(MoneyWithTax? other)
        {
            if (other is null) return false;
            return Price == other.Price && PriceVAT == other.PriceVAT && VAT == other.VAT;
        }

        public override bool Equals(object? obj) => Equals(obj as MoneyWithTax);

        public override int GetHashCode() => HashCode.Combine(Price, PriceVAT, VAT);

        public override string ToString() => $"Net: {Price}, VAT: {VAT}, Gross: {PriceVAT}";

        public static bool operator ==(MoneyWithTax? left, MoneyWithTax? right) => Equals(left, right);
        public static bool operator !=(MoneyWithTax? left, MoneyWithTax? right) => !Equals(left, right);
    }
}
