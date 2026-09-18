# Birko.Communication.Camera

Camera frame capture abstraction for the Birko Framework. Provides a unified interface for capturing still frames from camera devices, with an FFmpeg-based implementation that requires no NuGet dependencies.

## Features

- **ICameraSource** — common interface for camera capture (Open, Close, CaptureFrameAsync)
- **CapturedFrame** — JPEG frame data with width, height, timestamp, and size metadata
- **FfmpegCameraSource** — captures JPEG snapshots by shelling out to FFmpeg
  - Cross-platform: Linux (v4l2), Windows (dshow), macOS (avfoundation)
  - Configurable resolution, JPEG quality, device path, and FFmpeg binary path
  - No NuGet dependencies — only requires FFmpeg installed on the system

## Prerequisites

FFmpeg must be installed and available in PATH:

- **Linux:** `sudo apt install ffmpeg`
- **Windows:** `winget install ffmpeg` or `choco install ffmpeg`
- **macOS:** `brew install ffmpeg`

## Usage

```csharp
using Birko.Communication.Camera.Cameras;

var settings = new FfmpegCameraSettings
{
    Name = "USB Camera",
    Width = 1280,
    Height = 720,
    JpegQuality = 3
};

using var camera = new FfmpegCameraSource(settings);
camera.Open();

var frame = await camera.CaptureFrameAsync();
if (frame != null)
{
    Console.WriteLine($"Captured {frame.Width}x{frame.Height} frame, {frame.SizeBytes} bytes");
    File.WriteAllBytes("snapshot.jpg", frame.Data);
}
```

## Configuration

| Property | Default | Description |
|----------|---------|-------------|
| `Name` | "USB Camera" | Display name for the camera |
| `DevicePath` | auto-detect | Platform-specific device identifier |
| `InputFormat` | auto-detect | FFmpeg input format (v4l2/dshow/avfoundation) |
| `Width` | 640 | Frame width in pixels |
| `Height` | 480 | Frame height in pixels |
| `JpegQuality` | 5 | JPEG quality (1=best, 31=worst) |
| `FfmpegPath` | null (PATH) | Custom path to FFmpeg binary |

## Dependencies

- **Birko.Communication** — base communication interfaces

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
