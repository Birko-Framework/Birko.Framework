using System;

namespace Birko.Models.ValueObjects
{
    /// <summary>
    /// Immutable value object representing a postal address.
    /// </summary>
    public sealed class PostalAddress : IEquatable<PostalAddress>
    {
        public string Street { get; }
        public string StreetNumber { get; }
        public string City { get; }
        public string Zip { get; }
        public string Country { get; }
        public string? State { get; }

        public PostalAddress(string street, string streetNumber, string city, string zip, string country, string? state = null)
        {
            Street = street ?? throw new ArgumentNullException(nameof(street));
            StreetNumber = streetNumber ?? throw new ArgumentNullException(nameof(streetNumber));
            City = city ?? throw new ArgumentNullException(nameof(city));
            Zip = zip ?? throw new ArgumentNullException(nameof(zip));
            Country = country ?? throw new ArgumentNullException(nameof(country));
            State = state;
        }

        public bool Equals(PostalAddress? other)
        {
            if (other is null) return false;
            return Street == other.Street
                && StreetNumber == other.StreetNumber
                && City == other.City
                && Zip == other.Zip
                && Country == other.Country
                && State == other.State;
        }

        public override bool Equals(object? obj) => Equals(obj as PostalAddress);

        public override int GetHashCode() => HashCode.Combine(Street, StreetNumber, City, Zip, Country, State);

        public override string ToString()
        {
            var state = string.IsNullOrEmpty(State) ? string.Empty : $", {State}";
            return $"{Street} {StreetNumber}, {Zip} {City}{state}, {Country}";
        }

        public static bool operator ==(PostalAddress? left, PostalAddress? right) => Equals(left, right);
        public static bool operator !=(PostalAddress? left, PostalAddress? right) => !Equals(left, right);
    }
}
