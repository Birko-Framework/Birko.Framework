using System;
using Birko.Models.Customers;
using FluentAssertions;
using Xunit;
using CustomerAddressModel = Birko.Models.Customers.CustomerAddress;
using CustomerAddressViewModel = Birko.Models.Customers.ViewModels.CustomerAddress;
using AddressViewModel = Birko.Models.Customers.ViewModels.Address;

namespace Birko.Models.Customers.Tests;

/// <summary>
/// CR-H127: Customer.CopyTo must preserve all Customer-specific fields and the runtime type.
/// CR-H128: CustomerAddress.LoadFrom must populate the inherited Address fields from the nested VM.
/// </summary>
public class CustomerCopyAndLoadTests
{
    [Fact]
    public void Customer_CopyTo_PreservesTypeAndAllFields()
    {
        var source = new Customer
        {
            Name = "Acme",
            Code = "AC-1",
            Email = "a@b.com",
            Phone = "123",
            Website = "acme.test",
            PriceGroupGuid = Guid.NewGuid(),
            PartnerType = PartnerType.Both,
            LegalType = LegalType.Person,
            TaxId = "ICO-1",
            VatId = "DIC-1",
            Status = CustomerStatus.Blocked,
        };

        var clone = source.CopyTo(null!);

        clone.Should().BeOfType<Customer>("CopyTo must return the derived runtime type, not BaseCustomer");
        clone.Name.Should().Be("Acme");
        clone.Code.Should().Be("AC-1");
        clone.Email.Should().Be("a@b.com");
        clone.Phone.Should().Be("123");
        clone.Website.Should().Be("acme.test");
        clone.PriceGroupGuid.Should().Be(source.PriceGroupGuid);
        clone.PartnerType.Should().Be(PartnerType.Both);
        clone.LegalType.Should().Be(LegalType.Person);
        clone.TaxId.Should().Be("ICO-1");
        clone.VatId.Should().Be("DIC-1");
        clone.Status.Should().Be(CustomerStatus.Blocked);
    }

    [Fact]
    public void Customer_CopyTo_IntoExistingInstance_ReturnsSameInstance()
    {
        var source = new Customer { TaxId = "X" };
        var target = new Customer();

        var result = source.CopyTo(target);

        result.Should().BeSameAs(target);
        result.TaxId.Should().Be("X");
    }

    [Fact]
    public void CustomerAddress_LoadFrom_PopulatesAddressFieldsAndCustomerLink()
    {
        var customerGuid = Guid.NewGuid();
        var vm = new CustomerAddressViewModel
        {
            Guid = customerGuid,
            Address = new AddressViewModel
            {
                Name = "HQ",
                Street = "Main",
                StreetNumber = "1",
                City = "Town",
                ZIP = "12345",
                District = "D",
                Region = "R",
                Country = "CC",
                Phone = "555",
                Email = "hq@acme.test",
            },
        };

        var model = new CustomerAddressModel();
        model.LoadFrom(vm);

        model.CustomerGuid.Should().Be(customerGuid);
        model.Street.Should().Be("Main");
        model.StreetNumber.Should().Be("1");
        model.City.Should().Be("Town");
        model.ZIP.Should().Be("12345");
        model.District.Should().Be("D");
        model.Region.Should().Be("R");
        model.Country.Should().Be("CC");
        model.Phone.Should().Be("555");
        model.Email.Should().Be("hq@acme.test");
        model.Name.Should().Be("HQ");
    }

    [Fact]
    public void CustomerAddress_LoadFrom_NullAddress_KeepsCustomerLink_NoThrow()
    {
        var customerGuid = Guid.NewGuid();
        var vm = new CustomerAddressViewModel { Guid = customerGuid, Address = null };

        var model = new CustomerAddressModel();
        var act = () => model.LoadFrom(vm);

        act.Should().NotThrow();
        model.CustomerGuid.Should().Be(customerGuid);
        model.Street.Should().BeEmpty();
    }
}

/// <summary>
/// CR-L306: the watched-property → object-notification dispatch (now backed by a cached HashSet) must
/// still raise the aggregate "Address" notification when a watched property changes.
/// </summary>
public class CustomerViewModelNotificationTests
{
    [Fact]
    public void Address_WatchedPropertyChange_RaisesAddressObjectNotification()
    {
        var vm = new AddressViewModel();
        var raised = new System.Collections.Generic.List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.Name = "Acme";

        raised.Should().Contain(AddressViewModel.NameProperty);
        raised.Should().Contain(AddressViewModel.AddressObjectProperty);
    }
}
