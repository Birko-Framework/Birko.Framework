using Birko.Communication.Camera.Cameras;
using FluentAssertions;
using Xunit;

namespace Birko.Communication.Camera.Tests;

public class FfmpegCameraSettingsTests
{
    [Fact]
    public void DefaultValues_AreCorrect()
    {
        var settings = new FfmpegCameraSettings();

        settings.Name.Should().Be("USB Camera");
        settings.Width.Should().Be(640);
        settings.Height.Should().Be(480);
        settings.JpegQuality.Should().Be(5);
        settings.DevicePath.Should().BeNull();
        settings.InputFormat.Should().BeNull();
        settings.FfmpegPath.Should().BeNull();
    }

    [Fact]
    public void Properties_CanBeSet()
    {
        var settings = new FfmpegCameraSettings
        {
            Name = "HD Camera",
            Width = 1920,
            Height = 1080,
            JpegQuality = 2,
            DevicePath = "/dev/video1",
            InputFormat = "v4l2",
            FfmpegPath = "/usr/bin/ffmpeg"
        };

        settings.Name.Should().Be("HD Camera");
        settings.Width.Should().Be(1920);
        settings.Height.Should().Be(1080);
        settings.JpegQuality.Should().Be(2);
        settings.DevicePath.Should().Be("/dev/video1");
        settings.InputFormat.Should().Be("v4l2");
        settings.FfmpegPath.Should().Be("/usr/bin/ffmpeg");
    }
}
