# Birko.Communication.Camera

## Overview
Camera frame capture abstraction using FFmpeg. Provides ICameraSource interface and FfmpegCameraSource implementation for capturing JPEG snapshots from camera devices.

## Project Location
`Birko.Communication.Camera/`

## Components

### Cameras/ICameraSource.cs
- `ICameraSource` — interface: Name, IsOpen, Open(), Close(), CaptureFrameAsync()
- Extends IDisposable

### Cameras/CapturedFrame.cs
- `CapturedFrame` — immutable frame: Data (byte[]), Width, Height, Timestamp, SizeBytes

### Cameras/FfmpegCameraSource.cs
- `FfmpegCameraSource` — ICameraSource implementation using FFmpeg process
  - Cross-platform: v4l2 (Linux), dshow (Windows), avfoundation (macOS)
  - Captures single JPEG frame via `ffmpeg -frames:v 1 -f mjpeg pipe:1`
  - No NuGet dependencies
- `FfmpegCameraSettings` — configuration: Name, DevicePath, InputFormat, Width, Height, JpegQuality, FfmpegPath

## Dependencies
- Birko.Communication (base interfaces)
- System.Diagnostics.Process (FFmpeg execution)

## Namespace
`Birko.Communication.Camera.Cameras`

## Maintenance
- When adding new camera sources (e.g., OpenCV), implement ICameraSource
- Update .projitems with new files
- Update README.md with new implementations
