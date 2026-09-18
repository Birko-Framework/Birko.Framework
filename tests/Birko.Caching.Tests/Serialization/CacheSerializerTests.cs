using Birko.Caching.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.Caching.Tests.Serialization;

/// <summary>
/// Round-trip coverage for the CacheSerializer static helper (CR-L037: it had zero tests).
/// </summary>
public class CacheSerializerTests
{
    private sealed class Sample
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string[] Tags { get; set; } = System.Array.Empty<string>();
    }

    [Fact]
    public void Bytes_RoundTrip_PreservesValue()
    {
        var original = new Sample { Id = 7, Name = "widget", Tags = new[] { "a", "b" } };

        var bytes = CacheSerializer.Serialize(original);
        var restored = CacheSerializer.Deserialize<Sample>(bytes);

        bytes.Should().NotBeEmpty();
        restored.Should().NotBeNull();
        restored!.Id.Should().Be(7);
        restored.Name.Should().Be("widget");
        restored.Tags.Should().Equal("a", "b");
    }

    [Fact]
    public void String_RoundTrip_PreservesValue()
    {
        var original = new Sample { Id = 42, Name = "gadget" };

        var text = CacheSerializer.SerializeToString(original);
        var restored = CacheSerializer.DeserializeFromString<Sample>(text);

        text.Should().NotBeNullOrEmpty();
        restored!.Id.Should().Be(42);
        restored.Name.Should().Be("gadget");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(12345)]
    public void Bytes_RoundTrip_Primitives(int value)
    {
        CacheSerializer.Deserialize<int>(CacheSerializer.Serialize(value)).Should().Be(value);
    }

    [Fact]
    public void String_RoundTrip_String()
    {
        const string value = "hello, cache";
        CacheSerializer.DeserializeFromString<string>(CacheSerializer.SerializeToString(value)).Should().Be(value);
    }
}
