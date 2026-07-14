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

    [Theory]
    [InlineData(0, 1)]      // below range -> clamped to 1
    [InlineData(-5, 1)]
    [InlineData(1, 1)]
    [InlineData(15, 15)]    // in range -> unchanged
    [InlineData(31, 31)]
    [InlineData(100, 31)]   // above range -> clamped to 31
    public void JpegQuality_IsClampedToValidRange(int input, int expected)
    {
        // Regression for CR-L048: an out-of-range JpegQuality was forwarded to ffmpeg (-q:v) and
        // silently failed the capture; it is now clamped to [1,31] in the setter.
        var settings = new FfmpegCameraSettings { JpegQuality = input };
        settings.JpegQuality.Should().Be(expected);
    }
}
