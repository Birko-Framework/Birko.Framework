using Birko.Communication.Camera.Cameras;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Communication.Camera.Tests;

public class CapturedFrameTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var data = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 };
        var timestamp = new DateTime(2026, 3, 30, 12, 0, 0, DateTimeKind.Utc);

        var frame = new CapturedFrame(data, 640, 480, timestamp);

        frame.Data.Should().BeSameAs(data);
        frame.Width.Should().Be(640);
        frame.Height.Should().Be(480);
        frame.Timestamp.Should().Be(timestamp);
    }

    [Fact]
    public void SizeBytes_ReturnsDataLength()
    {
        var data = new byte[1024];
        var frame = new CapturedFrame(data, 320, 240, DateTime.UtcNow);

        frame.SizeBytes.Should().Be(1024);
    }

    [Fact]
    public void EmptyData_SizeBytesIsZero()
    {
        var frame = new CapturedFrame(Array.Empty<byte>(), 0, 0, DateTime.UtcNow);

        frame.SizeBytes.Should().Be(0);
    }
}
