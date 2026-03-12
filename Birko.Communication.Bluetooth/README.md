# Birko.Communication.Bluetooth

Bluetooth communication library providing Classic Bluetooth (via virtual COM port) and Bluetooth Low Energy (BLE) implementations for the Birko Framework.

## Features

- Classic Bluetooth communication through virtual serial port (extends `Serial`)
- Bluetooth Low Energy (BLE) with platform-specific support (Windows and Linux)
- BLE device discovery and scanning
- Configurable BLE settings (device address, service UUID, characteristic UUID)
- Auto-reconnection support for BLE connections
- Event-driven data reception

## Installation

This is a shared project (.projitems). Reference it from your main project:

```xml
<Import Project="..\Birko.Communication.Bluetooth\Birko.Communication.Bluetooth.projitems"
        Label="Shared" />
```

## Dependencies

- **Birko.Communication** - Base communication interfaces (`AbstractPort`, `PortSettings`)
- **Birko.Communication.Hardware** - `Serial` and `SerialSettings` base classes
- **Windows.Devices.Bluetooth** - Windows BLE API (when targeting Windows)
- **BlueZ/DBus** - Linux BLE support (when targeting Linux)

## Usage

### Classic Bluetooth (Virtual COM Port)

```csharp
using Birko.Communication.Bluetooth.Ports;

var settings = new BluetoothSettings
{
    Name = "BT-Device",
    BaudRate = 9600,
    Parity = Parity.None,
    DataBits = 8,
    StopBits = StopBits.One
};

var bt = new Bluetooth(settings);
bt.OnDataReceived += (sender, data) =>
{
    Console.WriteLine($"Received: {Encoding.UTF8.GetString(data)}");
};

bt.Open();
bt.Write(Encoding.UTF8.GetBytes("Hello"));
bt.Close();
```

### Bluetooth Low Energy

```csharp
using Birko.Communication.Bluetooth.Ports;

var settings = new BluetoothLESettings
{
    Name = "BLE-Sensor",
    DeviceAddress = "AA:BB:CC:DD:EE:FF",
    ServiceUuid = Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb"),
    CharacteristicUuid = Guid.Parse("00002a37-0000-1000-8000-00805f9b34fb"),
    ConnectionTimeout = 10000
};

var ble = new BluetoothLE(settings);
ble.OnDataReceived += (sender, data) =>
{
    // Handle BLE characteristic data
};

ble.Open();
```

### BLE Device Discovery

```csharp
using Birko.Communication.Bluetooth.Ports;

var devices = new BluetoothLEDevices();
var found = await devices.ScanAsync(TimeSpan.FromSeconds(5));

foreach (var device in found)
{
    Console.WriteLine($"{device.Name} ({device.Address}) RSSI: {device.Rssi}");
}
```

## API Reference

### Classes

| Class | Description |
|-------|-------------|
| `Bluetooth` | Classic Bluetooth port extending `Serial` (virtual COM) |
| `BluetoothSettings` | Settings for Classic Bluetooth (extends `SerialSettings`) |
| `BluetoothLE` | BLE port extending `AbstractPort` with Windows/Linux support |
| `BluetoothLESettings` | BLE settings (DeviceAddress, ServiceUuid, CharacteristicUuid, ConnectionTimeout) |
| `BluetoothLEDevices` | BLE device scanner and discovery |
| `DiscoveredDevice` | Represents a discovered BLE device (Name, Address, Rssi) |

### Namespace

- `Birko.Communication.Bluetooth.Ports`

## Related Projects

- [Birko.Communication](../Birko.Communication/) - Base communication abstractions
- [Birko.Communication.Hardware](../Birko.Communication.Hardware/) - Serial port base class

## License

Part of the Birko Framework.
