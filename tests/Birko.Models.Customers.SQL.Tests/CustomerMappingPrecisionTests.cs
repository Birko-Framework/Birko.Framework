using System.Linq;
using Birko.Models.Customers;
using Birko.Models.Customers.SQL.Mappings;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Models.Customers.SQL.Tests;

/// <summary>
/// CR-M219/M220: the Customer / Address / InvoiceAddress string columns were unmapped (unbounded,
/// unindexable) while the sibling ContactPersonMapping bounded all of its. These assert every string
/// column now carries a HasPrecision facet (readable via the mapping metadata), matching the convention.
/// </summary>
public class CustomerMappingPrecisionTests
{
    private static FieldDescriptorView Map<T>(IModelMapping<T> mapping) where T : class
    {
        var registry = new ModelMapRegistry();
        registry.Register(mapping);
        return new FieldDescriptorView(registry.GetPropertyMaps(typeof(T)));
    }

    private sealed class FieldDescriptorView
    {
        private readonly System.Collections.Generic.IReadOnlyList<Birko.Data.Patterns.Schema.FieldDescriptor> _fields;
        public FieldDescriptorView(System.Collections.Generic.IEnumerable<Birko.Data.Patterns.Schema.FieldDescriptor> fields)
            => _fields = fields.ToList();
        public int? Precision(string name) => _fields.FirstOrDefault(f => f.Name == name)?.Precision;
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Code")]
    [InlineData("Email")]
    [InlineData("Phone")]
    [InlineData("Website")]
    [InlineData("TaxId")]
    [InlineData("VatId")]
    public void CustomerMapping_BoundsStringColumns(string column)
    {
        Map(new CustomerMapping()).Precision(column).Should().HaveValue($"{column} must be bounded (CR-M219)");
    }

    [Theory]
    [InlineData("Name")]
    [InlineData("Street")]
    [InlineData("StreetNumber")]
    [InlineData("City")]
    [InlineData("ZIP")]
    [InlineData("District")]
    [InlineData("Region")]
    [InlineData("Country")]
    [InlineData("Phone")]
    [InlineData("Email")]
    public void AddressMapping_BoundsStringColumns(string column)
    {
        Map(new AddressMapping()).Precision(column).Should().HaveValue($"{column} must be bounded (CR-M220)");
    }

    [Theory]
    [InlineData("BIN")]
    [InlineData("TIN")]
    [InlineData("VATIN")]
    [InlineData("BankAccount")]
    [InlineData("Street")] // inherited address column, re-mapped
    public void InvoiceAddressMapping_BoundsStringColumns(string column)
    {
        Map(new InvoiceAddressMapping()).Precision(column).Should().HaveValue($"{column} must be bounded (CR-M220)");
    }
}
