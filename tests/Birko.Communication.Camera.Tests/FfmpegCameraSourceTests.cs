using Birko.Communication.Camera.Cameras;
using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Communication.Camera.Tests;

public class FfmpegCameraSourceTests
{
    [Fact]
    public void Constructor_NullSettings_Throws()
    {
        var act = () => new FfmpegCameraSource(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("settings");
    }

    [Fact]
    public void Open_SetsIsOpen()
    {
        using var source = new FfmpegCameraSource(new FfmpegCameraSettings());

        source.Open();

        source.IsOpen.Should().BeTrue();
    }

    [Fact]
    public void Close_ClearsIsOpen()
    {
        using var source = new FfmpegCameraSource(new FfmpegCameraSettings());
        source.Open();

        source.Close();

        source.IsOpen.Should().BeFalse();
    }

    [Fact]
    public async Task CaptureFrameAsync_WhenNotOpen_ReturnsNull()
    {
        using var source = new FfmpegCameraSource(new FfmpegCameraSettings());

        var frame = await source.CaptureFrameAsync();

        frame.Should().BeNull();
    }

    [Fact]
    public void Dispose_ClosesCamera()
    {
        var source = new FfmpegCameraSource(new FfmpegCameraSettings());
        source.Open();

        source.Dispose();

        source.IsOpen.Should().BeFalse();
    }

    [Fact]
    public void Name_ReturnsSettingsName()
    {
        var settings = new FfmpegCameraSettings { Name = "Test Camera" };
        using var source = new FfmpegCameraSource(settings);

        source.Name.Should().Be("Test Camera");
    }
}
