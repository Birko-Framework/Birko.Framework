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

> Issuing tokens (not just consuming them)? See `Birko.Security.OAuth.Server` for the authorization-server side — full /token, /authorize, /device_authorization, and dynamic client registration endpoints. Documented in [docs/security.md](security.md#oauth2-authorization-server-birkosecurityoauthserver).

### Birko.Communication.GraphQL
GraphQL client with zero external dependencies (HttpClient + ClientWebSocket + Birko.Serialization):
- **Queries and Mutations** — typed responses via `GraphQLResponse<T>` with automatic error handling
- **Subscriptions** — real-time data over WebSocket using graphql-ws protocol (`IObservable<T>`)
- **Static client caching** — `GraphQLClient.GetClient(endpoint)` with automatic per-URL caching
- **Fluent request builder** — `GraphQLRequestBuilder` with Query/Mutation/Variables/OperationName/Extensions
- **Event-driven** — OnRequest/OnResponse/OnError events for logging and monitoring
- **GraphQLSettings** — extends `RemoteSettings` (Endpoint=Location), adds SchemaPath, UseSubscriptions, SubscriptionProtocol, TimeoutSeconds, ExtraHeaders
- **ISerializer integration** — uses `Birko.Serialization.ISerializer` (SystemJsonSerializer) for all JSON

### Birko.Communication.gRPC
gRPC **client** primitives over `Grpc.Net.Client` (mirrors the REST / REST.Server split). Code generation (`.proto` → C#) is out of scope — bring generated clients via `Grpc.Tools`; these primitives configure and wrap them:
- **`GrpcChannelPool`** — endpoint-keyed cache of reusable, thread-safe `GrpcChannel`s (`GetChannel` / `Remove` / `Clear`)
- **`GrpcClientFactory`** — `CreateClient<TClient>()` builds any generated `ClientBase` over a pooled channel or explicit `CallInvoker`, applying interceptors in order
- **`GrpcAuthenticationInterceptor`** — client interceptor injecting auth metadata into all five call kinds; token-provider (bearer header + static metadata) or raw `Action<Metadata>` constructor
- **`GrpcSettings`** — extends `RemoteSettings` (Endpoint=Location), adds MaxReceive/SendMessageSizeBytes, DeadlineSeconds, Credentials, ExtraMetadata
- **`GrpcException`** — wraps `RpcException` → `StatusCode` / `Detail` / `Trailers`

### Birko.Communication.gRPC.Server
gRPC **server** primitives over `Grpc.AspNetCore` (requires the `Microsoft.AspNetCore.App` shared framework in the host):
- **`AddBirkoGrpc(this IServiceCollection, GrpcServerSettings?)`** — registers gRPC with Birko defaults (detailed errors, message-size caps); returns `IGrpcServerBuilder` for chaining
- **`GrpcServerAuthenticationInterceptor`** — server interceptor validating request metadata across all four handler kinds via `Func<Metadata, ServerCallContext, Task<bool>>`; throws `RpcException(Unauthenticated)` on failure
- **`GrpcServerSettings`** — extends `Settings`; `EnableDetailedErrors`, MaxReceive/SendMessageSizeBytes, `EnableReflection`

### Birko.Communication.AspNetCore
ASP.NET Core minimal-API helpers (requires the `Microsoft.AspNetCore.App` shared framework in the host). Ships the **owner-scoped CRUD** skeleton — the `GET /{id}` / `POST` / `PUT /{id}` / `DELETE /{id}` shape every per-user / per-tenant resource repeats — so the ownership guards are written once:
- **`MapOwnedCrud<TModel, TRequest, TRepo>(routeBase, mapping)`** — registers the four owner-scoped routes under a base path, resolving `TRepo` per request from DI; list endpoints are out of scope by design
- **`OwnedCrudResults`** — host-free (unit-testable) guard functions: `ReadOwned` (404 absent/foreign), `CreateClash` (409 own vs 404 foreign id), `RequireOwned` (PUT/DELETE gate) — the 404-vs-409 core that never leaks a foreign entity's existence
- **`OwnedCrudMapping<TModel, TRequest, TRepo>`** — per-resource config of `required` delegates (Owner/Read/Create/Update/Delete/OwnerOf/ToDto/Build/Apply; optional Validate/RequestGuid/Label)

### Birko.Communication.IR
Consumer infrared (38 kHz modulated) communication for remote control — **not** IrDA/IrCOMM (see `Birko.Communication.Hardware.Ports.Infraport` for serial IrCOMM):
- **InfraredPort** — extends AbstractPort with async send/receive and learning mode
- **Pluggable transports** via IIrTransport:
  - **SerialIrTransport** — USB-UART + IR LED on microcontroller (Arduino/ESP32)
  - **HttpIrTransport** — ESPHome REST API (remote_transmitter)
  - **MqttIrTransport** — ESPHome/Tasmota MQTT (stub)
  - **GpioIrTransport** — Linux /dev/lirc0, Raspberry Pi (stub)
- **Protocols** — IIrProtocol encode/decode between commands and raw timings:
  - **NecProtocol** — standard (8-bit address) and extended (16-bit), 38 kHz, 562.5 μs unit
  - **SamsungProtocol** — 32-bit TV remote (address repeated, command complemented)
  - **Rc5Protocol** — Philips Manchester-encoded 14-bit (36 kHz)
  - **RawProtocol** — capture & replay for unknown protocols (learning mode)
- **Device profiles** — IDeviceProfile codebook per device model:
  - **SamsungAcProfile** — Samsung AC (modes, temp 16–30 °C, fan speeds, swing, Wind-Free)

### Birko.Communication.NFC
NFC/RFID tag communication with pluggable transports and protocol handlers:
- **NfcReaderPort** — extends AbstractPort with async tag read, continuous polling, APDU passthrough
- **Pluggable transports** via INfcTransport:
  - **SerialNfcTransport** — UART readers (PN532, ACR122U serial mode)
  - **HttpNfcTransport** — REST API bridges (ESP32, Raspberry Pi)
  - **HidNfcTransport** — HID keyboard-emulation readers (enterprise badge readers)
- **Protocols** — INfcProtocol tag data parsing:
  - **Iso14443AProtocol** — SAK-based tag classification (MIFARE Classic/Ultralight/DESFire, NTAG)
  - **NdefProtocol** — NDEF message parser (URI, Text, TLV wrapper)
- **Models** — NfcTagData (UID, type, NDEF records), NdefRecord (URI/Text extraction), NfcTagType enum

## Usage

### Consumer IR via Serial Transport

```csharp
using Birko.Communication.IR.Ports;
using Birko.Communication.IR.Transports;
using Birko.Communication.IR.Protocols;

var settings = new InfraredSettings
{
    Name = "LivingRoom IR",
    TransportType = "serial",
    ConnectionString = "COM3"
};

var transport = new SerialIrTransport("COM3", 115200);
var port = new InfraredPort(settings, transport);
port.Open();

var nec = new NecProtocol();
await port.SendCommandAsync(nec, new IrCommand { Address = 0x04, Command = 0x02 });
port.Close();
```

### Samsung AC Control

```csharp
using Birko.Communication.IR.Devices;
using Birko.Communication.IR.Transports;

var transport = new HttpIrTransport("http://192.168.1.100");
await transport.ConnectAsync();

var ac = new SamsungAcProfile();
ac.SetTemperature(22);
ac.SetMode(SamsungAcMode.Cool);

var timing = ac.GetTiming("PowerOn");
await transport.TransmitAsync(timing!);
```

### IR Learning Mode

```csharp
port.RegisterProtocol(new NecProtocol());
port.RegisterProtocol(new SamsungProtocol());
port.RegisterProtocol(new RawProtocol()); // catch-all

port.OnCommandReceived += (sender, cmd) =>
    Console.WriteLine($"Received: {cmd}");

await port.StartLearningAsync();
// Point remote at receiver and press buttons...
await port.StopLearningAsync();
```

### OAuth2 Client Credentials

### GraphQL Query

```csharp
using Birko.Communication.GraphQL;

var client = new GraphQLClient(new GraphQLSettings
{
    Endpoint = "https://api.example.com/graphql"
});

var response = await client.QueryAsync<User>("{ user(id: 1) { name email } }");
Console.WriteLine(response.Data?.Name);

// With variables and operation name
var response2 = await client.QueryAsync<User>(
    "query GetUser($id: Int!) { user(id: $id) { name } }",
    new { id = 1 },
    "GetUser");
```

### GraphQL Mutation with Builder

```csharp
var request = new GraphQLRequestBuilder()
    .Mutation("mutation CreateUser($name: String!) { createUser(name: $name) { id name } }")
    .Variables(new { name = "Alice" })
    .OperationName("CreateUser")
    .Build();

var response = await client.ExecuteAsync<User>(request);
```

### GraphQL Subscription

```csharp
var client = new GraphQLClient(new GraphQLSettings
{
    Endpoint = "wss://api.example.com/graphql",
    UseSubscriptions = true
});

var subscription = await client.SubscribeAsync<Message>(
    "subscription { onMessage { text author } }");

subscription.AsObservable().Subscribe(msg =>
    Console.WriteLine($"{msg.Author}: {msg.Text}"));
```

### gRPC Client

```csharp
using Birko.Communication.gRPC;

var settings = new GrpcSettings { Endpoint = "https://api.example.com:443" };

// Bearer token injected on every call.
var auth = new GrpcAuthenticationInterceptor(() => tokenStore.CurrentAccessToken);

// Greeter.GreeterClient is generated from your .proto by Grpc.Tools.
var client = GrpcClientFactory.CreateClient<Greeter.GreeterClient>(settings, auth);

try
{
    var reply = await client.SayHelloAsync(new HelloRequest { Name = "Birko" });
}
catch (RpcException ex)
{
    throw GrpcException.FromRpcException(ex);
}
```

### gRPC Server

```csharp
using Birko.Communication.gRPC.Server;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddBirkoGrpc(new GrpcServerSettings { EnableDetailedErrors = true });

var app = builder.Build();
app.MapGrpcService<GreeterService>();   // GreeterService generated from your .proto
app.Run();
```

### OAuth2 Client Credentials (Original)

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

### NFC Tag Reading

```csharp
using Birko.Communication.NFC.Ports;
using Birko.Communication.NFC.Transports;
using Birko.Communication.NFC.Protocols;

var settings = new NfcReaderSettings
{
    Name = "Badge Reader",
    TransportType = "hid"
};

using var transport = new HidNfcTransport();
var port = new NfcReaderPort(settings, transport);
port.RegisterProtocol(new Iso14443AProtocol());
port.RegisterProtocol(new NdefProtocol());

port.Open();
port.OnTagDetected += (sender, tag) =>
    Console.WriteLine($"Tag: {tag.TagType} UID={tag.Uid}");

await port.StartPollingAsync();
```

### NFC Authentication

```csharp
using Birko.Security.NFC;

var store = new InMemoryNfcTagMappingStore();
var auth = new NfcAuthProvider(store);

// Enroll a badge
await auth.EnrollAsync(userId, "04A1B2C3", "Office badge", "John Doe");

// Authenticate on tap
var result = await auth.AuthenticateAsync("04A1B2C3");
if (result.IsAuthenticated)
    Console.WriteLine($"Welcome, {result.UserName}!");
```

## See Also

- [Birko.Communication](https://github.com/birko/Birko.Communication)
- [Birko.Communication.Network](https://github.com/birko/Birko.Communication.Network)
- [Birko.Communication.Hardware](https://github.com/birko/Birko.Communication.Hardware)
- [Birko.Communication.Bluetooth](https://github.com/birko/Birko.Communication.Bluetooth)
- [Birko.Communication.WebSocket](https://github.com/birko/Birko.Communication.WebSocket)
- [Birko.Communication.REST](https://github.com/birko/Birko.Communication.REST)
- [Birko.Communication.SOAP](https://github.com/birko/Birko.Communication.SOAP)
- [Birko.Communication.SSE](https://github.com/birko/Birko.Communication.SSE)
- [Birko.Communication.Modbus](https://github.com/birko/Birko.Communication.Modbus)
- [Birko.Communication.OAuth](https://github.com/birko/Birko.Communication.OAuth)
- [Birko.Communication.GraphQL](https://github.com/birko/Birko.Communication.GraphQL)
- [Birko.Communication.gRPC](https://github.com/birko/Birko.Communication.gRPC)
- [Birko.Communication.gRPC.Server](https://github.com/birko/Birko.Communication.gRPC.Server)
- [Birko.Communication.Camera](https://github.com/birko/Birko.Communication.Camera)
- [Birko.Communication.IR](https://github.com/birko/Birko.Communication.IR)
- [Birko.Communication.NFC](https://github.com/birko/Birko.Communication.NFC)
