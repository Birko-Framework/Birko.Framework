using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Birko.Communication.Camera.Cameras;

namespace Birko.Framework.Examples.Communication
{
    /// <summary>
    /// Usage examples for Birko.Communication.Camera
    /// </summary>
    public static class CameraExamples
    {
        /// <summary>
        /// Example: Capture a single JPEG snapshot
        /// </summary>
        public static async Task RunSnapshotExample()
        {
            var settings = new FfmpegCameraSettings
            {
                Name = "USB Camera",
                Width = 640,
                Height = 480,
                JpegQuality = 5
            };

            using var camera = new FfmpegCameraSource(settings);
            camera.Open();

            ExampleOutput.WriteLine($"Camera '{camera.Name}' opened, capturing frame...");

            var frame = await camera.CaptureFrameAsync();
            if (frame != null)
            {
                ExampleOutput.WriteLine($"Captured {frame.Width}x{frame.Height} frame, {frame.SizeBytes} bytes");
                ExampleOutput.WriteLine($"Timestamp: {frame.Timestamp:yyyy-MM-dd HH:mm:ss} UTC");

                // Save to file
                var path = Path.Combine(Path.GetTempPath(), "snapshot.jpg");
                File.WriteAllBytes(path, frame.Data);
                ExampleOutput.WriteLine($"Saved to: {path}");
            }
            else
            {
                ExampleOutput.WriteError("Failed to capture frame (is FFmpeg installed and camera connected?)");
            }
        }

        /// <summary>
        /// Example: Capture high-resolution frame
        /// </summary>
        public static async Task RunHighResExample()
        {
            var settings = new FfmpegCameraSettings
            {
                Name = "HD Camera",
                Width = 1920,
                Height = 1080,
                JpegQuality = 2 // High quality
            };

            using var camera = new FfmpegCameraSource(settings);
            camera.Open();

            ExampleOutput.WriteLine($"Capturing 1080p frame with quality={settings.JpegQuality}...");

            var frame = await camera.CaptureFrameAsync();
            if (frame != null)
            {
                ExampleOutput.WriteLine($"Frame: {frame.Width}x{frame.Height}, {frame.SizeBytes / 1024} KB");
            }
            else
            {
                ExampleOutput.WriteError("Failed to capture frame");
            }
        }

        /// <summary>
        /// Example: Periodic capture (time-lapse)
        /// </summary>
        public static async Task RunTimeLapseExample()
        {
            var settings = new FfmpegCameraSettings
            {
                Name = "Time-lapse Camera",
                Width = 1280,
                Height = 720,
                JpegQuality = 5
            };

            using var camera = new FfmpegCameraSource(settings);
            camera.Open();

            var outputDir = Path.Combine(Path.GetTempPath(), "timelapse");
            Directory.CreateDirectory(outputDir);

            ExampleOutput.WriteLine($"Starting time-lapse capture to {outputDir}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var frameCount = 0;

            while (!cts.Token.IsCancellationRequested)
            {
                var frame = await camera.CaptureFrameAsync(cts.Token);
                if (frame != null)
                {
                    frameCount++;
                    var filename = $"frame_{frameCount:D4}.jpg";
                    File.WriteAllBytes(Path.Combine(outputDir, filename), frame.Data);
                    ExampleOutput.WriteLine($"  Captured {filename} ({frame.SizeBytes / 1024} KB)");
                }

                try
                {
                    await Task.Delay(5000, cts.Token); // 5 second interval
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            ExampleOutput.WriteLine($"Time-lapse complete: {frameCount} frames captured");
        }

        /// <summary>
        /// Example: Custom FFmpeg path and device
        /// </summary>
        public static async Task RunCustomDeviceExample()
        {
            // Linux: specific video device
            var linuxSettings = new FfmpegCameraSettings
            {
                Name = "External USB Camera",
                DevicePath = "/dev/video1",
                InputFormat = "v4l2",
                Width = 640,
                Height = 480
            };

            // Windows: named camera device
            var windowsSettings = new FfmpegCameraSettings
            {
                Name = "Laptop Camera",
                DevicePath = "video=\"Integrated Camera\"",
                InputFormat = "dshow",
                Width = 640,
                Height = 480,
                FfmpegPath = @"C:\tools\ffmpeg\bin\ffmpeg.exe"
            };

            // Use platform-appropriate settings
            var settings = Environment.OSVersion.Platform == PlatformID.Win32NT
                ? windowsSettings
                : linuxSettings;

            ExampleOutput.WriteLine($"Using camera: {settings.Name}");
            ExampleOutput.WriteLine($"  Device: {settings.DevicePath}");
            ExampleOutput.WriteLine($"  Format: {settings.InputFormat}");

            using var camera = new FfmpegCameraSource(settings);
            camera.Open();

            var frame = await camera.CaptureFrameAsync();
            ExampleOutput.WriteLine(frame != null
                ? $"Captured {frame.Width}x{frame.Height} frame"
                : "No frame captured");
        }
    }
}
