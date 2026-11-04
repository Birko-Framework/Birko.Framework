using System.IO;
using System.Text;
using Birko.Data.CosmosDB.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// TASK-723. Measured before the fix: <c>CosmosGuidIdSerializer.FromStream&lt;Stream&gt;</c> returned the stream from inside
/// <c>using (stream)</c>, so the caller was handed a disposed stream. A stream asked for as a stream is handed over open;
/// the serializer disposes only what it reads itself.
/// </summary>
public class Task723SerializerStreamTests
{
    [Fact]
    public void A_stream_asked_for_as_a_stream_comes_back_readable()
    {
        var payload = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}"));

        var result = new CosmosGuidIdSerializer().FromStream<Stream>(payload);

        result.CanRead.Should().BeTrue();
        new StreamReader(result).ReadToEnd().Should().Be("{\"a\":1}");
    }

    [Fact]
    public void A_stream_it_reads_is_disposed()
    {
        var payload = new MemoryStream(Encoding.UTF8.GetBytes("{\"a\":1}"));

        new CosmosGuidIdSerializer().FromStream<System.Collections.Generic.Dictionary<string, int>>(payload)["a"].Should().Be(1);

        payload.CanRead.Should().BeFalse();
    }
}
