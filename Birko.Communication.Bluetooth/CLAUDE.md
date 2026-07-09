# Birko.Communication.Bluetooth

## Overview
Bluetooth communication implementation for Birko.Communication.

## Purpose
- Classic Bluetooth communication over a Virtual COM Port (SPP)
- Bluetooth Low Energy (BLE) client port with Windows and Linux backends
- BLE device discovery

## Components
All types live in namespace `Birko.Communication.Bluetooth.Ports`. There is **no** server/peripheral
API and no `*Communicator`/`BLEScanner`/`BLEServer`/`BLEService`/`BLECharacteristic` type — this is a
client/port library only.

### Classic Bluetooth (Virtual COM Port)
- `Bluetooth : Serial` — SPP wrapper; behaves like a serial port.
- `BluetoothSettings : SerialSettings` — `Name`, `BaudRate`, `Parity`, `DataBits`, `StopBits`.

### BLE
- `BluetoothLE : AbstractPort, IDisposable` — a BLE client **port** (`Open`/`Close`/`Write`/`Read`/
  `HasReadData`/`RemoveReadData`), platform-gated (`#if WINDOWS` WinRT / `#if LINUX` L2CAP socket;
  throws `PlatformNotSupportedException` elsewhere). Optional `AutoReconnect`.
- `BluetoothLESettings : PortSettings` — `DeviceAddress`, `ServiceUuid`, `CharacteristicUuid`,
  `ConnectionTimeout`, `AutoReconnect`, `MaxReconnectAttempts`.

### Discovery
- `BluetoothLEDevices` — **static** class: `DiscoverDevicesAsync(timeout, ct)` and
  `DiscoverDevicesWithServiceAsync(serviceUuid, timeout, ct)`. (Service-filtered discovery is not
  implemented on the Linux bluetoothctl backend and throws `NotSupportedException` there — CR-M041.)
- `DiscoveredDevice` — `Name`, `Address`, `Rssi`.

## Classic Bluetooth (VCP)

```csharp
using Birko.Communication.Bluetooth.Ports;

var bt = new Bluetooth(new BluetoothSettings { Name = "COM5", BaudRate = 9600 });
bt.Open();
bt.Write(System.Text.Encoding.UTF8.GetBytes("Hello"));
var reply = bt.Read(-1); // -1 = all available
bt.Close();
```

## BLE client

```csharp
using Birko.Communication.Bluetooth.Ports;

var ble = new BluetoothLE(new BluetoothLESettings
{
    Name = "BLE-Sensor",
    DeviceAddress = "AA:BB:CC:DD:EE:FF",
    ConnectionTimeout = 10000,
    AutoReconnect = true
});

// AbstractPort delivers data via a subscription callback; pull bytes with Read/RemoveReadData.
ble.SubscribeProcessData(() =>
{
    var data = ble.RemoveReadData(-1);
    // handle data
});

ble.Open();   // throws PlatformNotSupportedException off Windows/Linux
// ...
ble.Close();
ble.Dispose();
```

## BLE discovery

```csharp
var devices = await BluetoothLEDevices.DiscoverDevicesAsync(TimeSpan.FromSeconds(5));
foreach (var d in devices)
    Console.WriteLine($"{d.Name} ({d.Address}) RSSI: {d.Rssi}");
```

## Dependencies
- Birko.Communication
- Platform-specific Bluetooth libraries

## Platform Support

### Windows
- Windows.Devices.Bluetooth
- Windows Bluetooth APIs

### Linux
- BlueZ
- DBus

### Android/iOS
- Platform-specific APIs

## Use Cases
- IoT device communication
- Wearables
- Smart home devices
- Healthcare devices
- Asset tracking

## Best Practices

1. **Permissions** - Request proper Bluetooth permissions
2. **Discovery** - Implement proper device discovery
3. **Pairing** - Handle pairing/bonding flow
4. **Power** - Consider power consumption for BLE
5. **Error handling** - Handle connection drops gracefully

## Maintenance

### README Updates
When making changes that affect the public API, features, or usage patterns of this project, update the README.md accordingly. This includes:
- New classes, interfaces, or methods
- Changed dependencies
- New or modified usage examples
- Breaking changes

### CLAUDE.md Updates
When making major changes to this project, update this CLAUDE.md to reflect:
- New or renamed files and components
- Changed architecture or patterns
- New dependencies or removed dependencies
- Updated interfaces or abstract class signatures
- New conventions or important notes

### Test Requirements
Every new public functionality must have corresponding unit tests. When adding new features:
- Create test classes in the corresponding test project
- Follow existing test patterns (xUnit + FluentAssertions)
- Test both success and failure cases
- Include edge cases and boundary conditions
