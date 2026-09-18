using System;

namespace Birko.Models.ValueObjects
{
    /// <summary>
    /// Immutable value object representing a percentage value.
    /// Replaces AbstractPercentage's decimal property.
    /// </summary>
    public sealed class Percentage : IEquatable<Percentage>
    {
        public decimal Value { get; }

        public Percentage(decimal value)
        {
            Value = value;
        }

        public static Percentage Zero => new Percentage(0m);

        /// <summary>
        /// Applies this percentage to the given amount.
        /// E.g., 20% of 100 returns 20.
        /// </summary>
        public decimal ApplyTo(decimal amount) => amount * Value / 100m;

        /// <summary>
        /// Adds this percentage to the given amount.
        /// E.g., 100 + 20% returns 120.
        /// </summary>
        public decimal AddTo(decimal amount) => amount + ApplyTo(amount);

        public bool Equals(Percentage? other)
        {
            if (other is null) return false;
            return Value == other.Value;
        }

        public override bool Equals(object? obj) => Equals(obj as Percentage);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString() => $"{Value}%";

        public static bool operator ==(Percentage? left, Percentage? right) => Equals(left, right);
        public static bool operator !=(Percentage? left, Percentage? right) => !Equals(left, right);

        public static implicit operator decimal(Percentage p) => p.Value;
        public static explicit operator Percentage(decimal d) => new Percentage(d);
    }
}
