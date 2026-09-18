using System;

namespace Birko.Models.ValueObjects
{
    /// <summary>
    /// Immutable value object representing an amount with a unit of measure.
    /// </summary>
    public sealed class Quantity : IEquatable<Quantity>
    {
        public decimal Amount { get; }
        public string Unit { get; }

        public Quantity(decimal amount, string unit)
        {
            if (string.IsNullOrWhiteSpace(unit))
            {
                throw new ArgumentException("Unit is required.", nameof(unit));
            }

            Amount = amount;
            Unit = unit;
        }

        public static Quantity Zero(string unit) => new Quantity(0m, unit);

        public Quantity Add(Quantity other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Unit != other.Unit)
            {
                throw new InvalidOperationException($"Cannot add {Unit} and {other.Unit}.");
            }

            return new Quantity(Amount + other.Amount, Unit);
        }

        public Quantity Subtract(Quantity other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            if (Unit != other.Unit)
            {
                throw new InvalidOperationException($"Cannot subtract {other.Unit} from {Unit}.");
            }

            return new Quantity(Amount - other.Amount, Unit);
        }

        public Quantity Multiply(decimal factor) => new Quantity(Amount * factor, Unit);

        public bool Equals(Quantity? other)
        {
            if (other is null) return false;
            return Amount == other.Amount && Unit == other.Unit;
        }

        public override bool Equals(object? obj) => Equals(obj as Quantity);

        public override int GetHashCode() => HashCode.Combine(Amount, Unit);

        public override string ToString() => $"{Amount} {Unit}";

        public static bool operator ==(Quantity? left, Quantity? right) => Equals(left, right);
        public static bool operator !=(Quantity? left, Quantity? right) => !Equals(left, right);
    }
}
