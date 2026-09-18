# Birko.Communication.NFC

NFC/RFID tag communication library for the Birko Framework. Provides pluggable transport backends for reading NFC tags, parsing tag data, and NDEF message handling.

## Features

- **Pluggable transports** — Serial (PN532, ACR122U), HTTP (IoT bridges), HID (keyboard emulation readers)
- **Protocol handlers** — ISO 14443A tag classification, NDEF message parsing
- **Tag detection** — Single read, continuous polling with events
- **NDEF support** — Parse URI, Text, and raw NDEF records from NFC Forum tags
- **APDU passthrough** — Send raw APDU commands to tags via Serial/HTTP transports
- **Tag type detection** — MIFARE Classic, Ultralight, DESFire, NTAG, FeliCa, EM4100, HID Prox

## Installation

Add the shared project reference to your `.csproj`:

```xml
<Import Project="..\Birko.Communication.NFC\Birko.Communication.NFC.projitems" Label="Shared" />
```

## Dependencies

- **Birko.Communication** — Base port/settings abstractions
- **System.IO.Ports** — Serial transport (NuGet: `System.IO.Ports`)
- **System.Net.Http** — HTTP transport
- **System.Text.Json** — HTTP response parsing

## Usage

### Read a tag via Serial (PN532/ACR122U)

```csharp
using Birko.Communication.NFC.Ports;
using Birko.Communication.NFC.Transports;
using Birko.Communication.NFC.Protocols;

var settings = new NfcReaderSettings
{
    Name = "Office Reader",
    TransportType = "serial",
    ConnectionString = "COM3",
    ReadTimeoutMs = 5000
};

using var transport = new SerialNfcTransport("COM3");
var port = new NfcReaderPort(settings, transport);
port.RegisterProtocol(new Iso14443AProtocol());
port.RegisterProtocol(new NdefProtocol());

port.Open();
var tag = await port.ReadTagAsync();
if (tag != null)
{
    Console.WriteLine($"Tag: {tag.TagType} UID={tag.Uid}");
    foreach (var record in tag.NdefRecords)
    {
        var uri = record.GetUri();
        if (uri != null) Console.WriteLine($"  URI: {uri}");
    }
}
port.Close();
```

### Continuous polling with events

```csharp
port.OnTagDetected += (sender, tag) =>
{
    Console.WriteLine($"Tag detected: {tag.Uid}");
};
port.OnTagRemoved += (sender, e) =>
{
    Console.WriteLine("Tag removed");
};

await port.StartPollingAsync();
// ... tag events fire as cards are presented/removed
await port.StopPollingAsync();
```

### HID keyboard-emulation reader (kiosk/terminal)

```csharp
using var transport = new HidNfcTransport();
await transport.ConnectAsync();

transport.TagDetected += (sender, tag) =>
{
    Console.WriteLine($"Badge scanned: {tag.Uid}");
};

await transport.StartPollingAsync(250);

// Feed keystrokes from your UI layer:
await transport.FeedInputAsync("04A1B2C3\n");
```

### HTTP IoT bridge (ESP32/Raspberry Pi)

```csharp
using var transport = new HttpNfcTransport("http://192.168.1.100:8080");
await transport.ConnectAsync();

var tag = await transport.ReadTagAsync(5000);
```

## API Reference

### Models

| Class | Description |
|-------|-------------|
| `NfcTagData` | Tag UID, type, NDEF records, payload, metadata |
| `NfcTagType` | Enum: ISO14443A/B, MIFARE Classic/Ultralight/DESFire, NTAG, FeliCa, EM4100, HID Prox |
| `NdefRecord` | NDEF record with TNF, type, payload. Helpers: `GetUri()`, `GetText()` |
| `NdefTnf` | NDEF Type Name Format enum |

### Ports

| Class | Description |
|-------|-------------|
| `NfcReaderSettings` | Transport type, connection string, timeouts, polling interval |
| `NfcReaderPort` | Main port — wraps transport + protocols, fires tag events |

### Transports

| Class | Description |
|-------|-------------|
| `INfcTransport` | Transport interface: connect, read, poll, APDU transceive |
| `SerialNfcTransport` | UART reader (PN532 protocol) |
| `HttpNfcTransport` | REST API reader (IoT bridges) |
| `HidNfcTransport` | HID keyboard emulation (enterprise badge readers) |

### Protocols

| Class | Description |
|-------|-------------|
| `INfcProtocol` | Protocol handler interface |
| `Iso14443AProtocol` | ISO 14443A: SAK-based tag classification, ATQA parsing |
| `NdefProtocol` | NDEF message parser (TLV, records, URI/Text extraction) |

## Related Projects

- [Birko.Communication](../Birko.Communication/README.md) — Base communication abstractions
- [Birko.Communication.Hardware](../Birko.Communication.Hardware/README.md) — Serial port implementation
- [Birko.Communication.IR](../Birko.Communication.IR/README.md) — Similar architecture for IR communication
- [Birko.Security.NFC](../Birko.Security.NFC/README.md) — NFC-based authentication (tag-to-user mapping)

## License

See [License.md](License.md) for details.
