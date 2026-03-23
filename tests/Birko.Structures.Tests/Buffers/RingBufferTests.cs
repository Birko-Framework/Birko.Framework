using Birko.Structures.Buffers;
using FluentAssertions;
using Xunit;

namespace Birko.Structures.Tests.Buffers;

public class RingBufferTests
{
    [Fact]
    public void Write_And_Read()
    {
        var buf = new RingBuffer<int>(3);
        buf.Write(1); buf.Write(2); buf.Write(3);
        buf.Count.Should().Be(3);
        buf.Read().Should().Be(1);
        buf.Read().Should().Be(2);
    }

    [Fact]
    public void Overwrites_Oldest_When_Full()
    {
        var buf = new RingBuffer<int>(3);
        buf.Write(1); buf.Write(2); buf.Write(3);
        buf.Write(4); // overwrites 1
        buf.Count.Should().Be(3);
        buf.Read().Should().Be(2);
    }

    [Fact]
    public void ToArray_ReturnsInOrder()
    {
        var buf = new RingBuffer<int>(5);
        buf.Write(10); buf.Write(20); buf.Write(30);
        buf.ToArray().Should().Equal(10, 20, 30);
    }

    [Fact]
    public void Read_OnEmpty_Throws()
    {
        var buf = new RingBuffer<int>(3);
        var act = () => buf.Read();
        act.Should().Throw<InvalidOperationException>();
    }
}
