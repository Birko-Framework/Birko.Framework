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
    public void Read_WithNoInit_ShouldReturnEmpty()
    {
        var store = new Birko.Data.XML.Stores.XmlStore<TestModel>();
        var result = store.Read();
        result.Should().BeEmpty();
    }

    [Fact]
    public void Count_WithNoInit_ShouldReturnZero()
    {
        var store = new Birko.Data.XML.Stores.XmlStore<TestModel>();
        var result = store.Count();
        result.Should().Be(0);
    }

    [Fact]
    public void Read_WithEmptyGuid_ShouldReturnNull()
    {
        var store = new Birko.Data.XML.Stores.XmlStore<TestModel>();
        var result = store.Read(Guid.Empty);
        result.Should().BeNull();
    }
}
