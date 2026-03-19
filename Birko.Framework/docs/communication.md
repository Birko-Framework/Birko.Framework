# Communication

The Birko.Communication layer provides abstractions and implementations for various communication protocols and hardware interfaces.

## Projects

### Birko.Communication
Base communication interfaces and abstractions shared across all communication implementations.

### Birko.Communication.Network
Network communication utilities (TCP/UDP sockets, network discovery).

### Birko.Communication.Hardware
Hardware communication interfaces (serial ports, device I/O).

### Birko.Communication.Bluetooth
Bluetooth communication (device discovery, pairing, data transfer).

### Birko.Communication.WebSocket
WebSocket client/server implementation with middleware support.

### Birko.Communication.REST
REST API client with typed requests/responses, authentication, and retry support.

### Birko.Communication.SOAP
SOAP client for consuming WSDL-based web services.

### Birko.Communication.SSE
Server-Sent Events (SSE) client for real-time server push.

### Birko.Communication.Modbus
Modbus RTU (serial) and Modbus TCP (network) communication for industrial devices:
- Function codes: 01-06 (read/write coils and registers), 15-16 (write multiple)
- CRC-16 validation for RTU frames
- Configurable timeouts and retry

### Birko.Communication.Camera
Camera frame capture abstraction:
- **ICameraSource** — common interface (Open, Close, CaptureFrameAsync)
- **CapturedFrame** — JPEG frame data with metadata (width, height, timestamp)
- **FfmpegCameraSource** — captures JPEG snapshots via FFmpeg process
  - Cross-platform: Linux (v4l2), Windows (dshow), macOS (avfoundation)
  - No NuGet dependencies — requires FFmpeg installed on the system

## Usage

### Camera Capture

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
    File.WriteAllBytes("snapshot.jpg", frame.Data);
}
```

### Modbus RTU

```csharp
using Birko.Communication.Modbus;

var settings = new ModbusRtuSettings
{
    PortName = "/dev/ttyUSB0",
    BaudRate = 9600
};

using var client = new ModbusRtuClient(settings);
client.Open();

// Read holding registers (function code 03)
var registers = await client.ReadHoldingRegistersAsync(slaveId: 1, startAddress: 0, count: 10);
```
