# Birko.Communication.Bluetooth

## Overview
Bluetooth communication implementation for Birko.Communication.

## Project Location
`C:\Source\Birko.Communication.Bluetooth\`

## Purpose
- Classic Bluetooth communication
- Bluetooth Low Energy (BLE)
- Device discovery and pairing
- Bluetooth protocol implementation

## Components

### Classic Bluetooth
- `BluetoothCommunicator` - Classic Bluetooth
- `BluetoothServer` - Bluetooth server

### BLE
- `BLECommunicator` - BLE client
- `BLEServer` - BLE peripheral
- `BLEService` - BLE service definition
- `BLECharacteristic` - BLE characteristic

### Discovery
- `BluetoothDeviceFinder` - Device discovery
- `BLEScanner` - BLE scanning

## Classic Bluetooth

```csharp
using Birko.Communication.Bluetooth;

var communicator = new BluetoothCommunicator(deviceAddress);
communicator.Connect();
communicator.Send(data);
```

## BLE Client

```csharp
var scanner = new BLEScanner();
var devices = await scanner.ScanAsync(TimeSpan.FromSeconds(5));

var device = devices.First(d => d.Name == "MyDevice");
var client = new BLECommunicator(device);

await client.ConnectAsync();
var service = await client.GetServiceAsync(serviceUuid);
var characteristic = await service.GetCharacteristicAsync(characteristicUuid);

await characteristic.WriteAsync(data);
```

## BLE Server (Peripheral)

```csharp
var server = new BLEServer();
var service = new BLEService(serviceUuid);

var characteristic = new BLECharacteristic(characteristicUuid)
{
    CanRead = true,
    CanWrite = true,
    CanNotify = true
};

characteristic.OnWrite = (data) =>
{
    // Handle write
};

service.AddCharacteristic(characteristic);
server.AddService(service);

await server.StartAsync();
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
