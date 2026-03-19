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

### Birko.Communication.OAuth
OAuth2 client library supporting multiple grant types with automatic token caching and refresh:
- **Client Credentials** — machine-to-machine authentication
- **Authorization Code** — server-side web apps with confidential client
- **Authorization Code + PKCE** — public clients (SPAs, mobile, CLI)
- **Device Code** — input-constrained devices (CLI, IoT, smart TV)
- **Refresh Token** — automatic and manual token refresh
- **OAuthDelegatingHandler** — automatic Bearer token injection for HttpClient with 401 retry
- **PkceChallenge** — built-in SHA-256 PKCE challenge pair generation
- **OAuthSettings** — extends `RemoteSettings` (ClientId=UserName, ClientSecret=Password, TokenEndpoint=Location)

## Usage

### OAuth2 Client Credentials

```csharp
using Birko.Communication.OAuth;

var settings = new OAuthSettings
{
    TokenEndpoint = "https://auth.example.com/oauth/token",
    ClientId = "my-client-id",
    ClientSecret = "my-client-secret",
    Scope = "api.read api.write",
    GrantType = OAuthGrantType.ClientCredentials
};

using var oauthClient = new OAuthClient(settings);
var token = await oauthClient.GetTokenAsync(); // Cached + auto-refreshed

// Use with HttpClient via DelegatingHandler
var httpClient = new HttpClient(new OAuthDelegatingHandler(oauthClient)
{
    InnerHandler = new HttpClientHandler()
});
var response = await httpClient.GetAsync("https://api.example.com/data");
```

### OAuth2 Device Code Flow

```csharp
var settings = new OAuthSettings
{
    TokenEndpoint = "https://auth.example.com/oauth/token",
    DeviceAuthorizationEndpoint = "https://auth.example.com/oauth/device/code",
    ClientId = "my-device-client",
    Scope = "openid profile",
    GrantType = OAuthGrantType.DeviceCode
};

using var client = new OAuthClient(settings);
var deviceAuth = await client.RequestDeviceAuthorizationAsync();
Console.WriteLine($"Visit {deviceAuth.VerificationUri} and enter code: {deviceAuth.UserCode}");
var token = await client.PollDeviceTokenAsync(deviceAuth.DeviceCode);
```

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
