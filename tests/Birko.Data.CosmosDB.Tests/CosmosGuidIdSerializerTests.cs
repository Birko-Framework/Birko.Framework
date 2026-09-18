using Birko.Data.CosmosDB.Serialization;
using FluentAssertions;
using System;
using System.IO;
using System.Text.Json;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// Regression tests for CR-C04: AbstractModel exposes only "Guid" (never "id"), so Cosmos point
/// reads/writes keyed by guid.ToString() against the '/id' partition key could never resolve. The
/// custom CosmosGuidIdSerializer injects an 'id' equal to the model's Guid on write. Validated in
/// isolation (no Cosmos emulator — the point-operation behavior is the documented infra gap).
/// </summary>
public class CosmosGuidIdSerializerTests
{
    private static string ReadAll(Stream s)
    {
        s.Position = 0;
        using var reader = new StreamReader(s);
        return reader.ReadToEnd();
    }

    [Fact]
    public void ToStream_InjectsIdEqualToGuid()
    {
        var serializer = new CosmosGuidIdSerializer();
        var guid = Guid.NewGuid();
        var model = new TestModel { Guid = guid, Name = "alpha", Value = 42 };

        var json = ReadAll(serializer.ToStream(model));

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("id", out var id).Should().BeTrue("the document must carry a Cosmos 'id'");
        id.GetString().Should().Be(guid.ToString());
    }

    [Fact]
    public void ToStream_ThenFromStream_RoundTripsTheModel()
    {
        var serializer = new CosmosGuidIdSerializer();
        var guid = Guid.NewGuid();
        var model = new TestModel { Guid = guid, Name = "beta", Value = 7 };

        var stream = serializer.ToStream(model);
        var back = serializer.FromStream<TestModel>(stream);

        back.Should().NotBeNull();
        back!.Guid.Should().Be(guid);
        back.Name.Should().Be("beta");
        back.Value.Should().Be(7);
    }

    [Fact]
    public void ToStream_NonModelType_DoesNotGetAnId()
    {
        var serializer = new CosmosGuidIdSerializer();
        var json = ReadAll(serializer.ToStream(new { foo = "bar" }));

        using var doc = JsonDocument.Parse(json);
        doc.RootElement.TryGetProperty("id", out _).Should().BeFalse();
    }
}
