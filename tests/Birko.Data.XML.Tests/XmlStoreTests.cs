using Birko.Data.Models;
using FluentAssertions;
using System;
using System.IO;
using Xunit;

namespace Birko.Data.XML.Tests;

public class TestModel : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}

public class XmlStoreTests
{
    [Fact]
    public void Constructor_Default_ShouldNotThrow()
    {
        var store = new Birko.Data.XML.Stores.XmlStore<TestModel>();
        store.Should().NotBeNull();
    }

    [Fact]
    public void Read_WithNoInit_IsRefused()
    {
        var store = new Birko.Data.XML.Stores.XmlStore<TestModel>();

        // TASK-812 (rule 56, inverted): a store that was never configured refuses, reads included; it answered empty.
        FluentActions.Invoking(() => store.Read()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Count_WithNoInit_IsRefused()
    {
        var store = new Birko.Data.XML.Stores.XmlStore<TestModel>();

        // TASK-812 (rule 56, inverted): a store that was never configured refuses, reads included; it answered empty.
        FluentActions.Invoking(() => store.Count()).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Read_WithEmptyGuid_IsRefused()
    {
        var store = new Birko.Data.XML.Stores.XmlStore<TestModel>();

        // TASK-812 (rule 56, inverted): a store that was never configured refuses, reads included; it answered empty.
        FluentActions.Invoking(() => store.Read(Guid.Empty)).Should().Throw<InvalidOperationException>();
    }
}
