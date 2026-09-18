using System;

namespace Birko.Models.ValueObjects
{
    /// <summary>
    /// Immutable value object representing a monetary amount with currency.
    /// Replaces scattered decimal fields across models.
    /// </summary>
    public sealed class Money : IEquatable<Money>
    {
        public decimal Amount { get; }
        public string CurrencyCode { get; }

        public Money(decimal amount, string currencyCode)
        {
            if (string.IsNullOrWhiteSpace(currencyCode))
            {
                throw new ArgumentException("Currency code is required.", nameof(currencyCode));
            }

            Amount = amount;
            CurrencyCode = currencyCode;
        }

        public static Money Zero(string currencyCode) => new Money(0m, currencyCode);

        public Money Add(Money other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (CurrencyCode != other.CurrencyCode)
            {
                throw new InvalidOperationException($"Cannot add {CurrencyCode} and {other.CurrencyCode}.");
            }

            return new Money(Amount + other.Amount, CurrencyCode);
        }

        public Money Subtract(Money other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (CurrencyCode != other.CurrencyCode)
            {
                throw new InvalidOperationException($"Cannot subtract {other.CurrencyCode} from {CurrencyCode}.");
            }

            return new Money(Amount - other.Amount, CurrencyCode);
        }

        public Money Multiply(decimal factor) => new Money(Amount * factor, CurrencyCode);

        public Money Round(int decimals) => new Money(Math.Round(Amount, decimals), CurrencyCode);

        public bool Equals(Money? other)
        {
            if (other is null) return false;
            return Amount == other.Amount && CurrencyCode == other.CurrencyCode;
        }

        public override bool Equals(object? obj) => Equals(obj as Money);

        public override int GetHashCode() => HashCode.Combine(Amount, CurrencyCode);

        public override string ToString() => $"{Amount} {CurrencyCode}";

        public static bool operator ==(Money? left, Money? right) => Equals(left, right);
        public static bool operator !=(Money? left, Money? right) => !Equals(left, right);
    }
}
