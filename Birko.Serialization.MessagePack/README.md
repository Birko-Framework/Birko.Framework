# Birko.Serialization.MessagePack

MessagePack binary serialization implementation of `ISerializer` for the Birko Framework.

## Features

- Compact binary format for high-performance scenarios
- Default: ContractlessStandardResolver (no attributes required)
- String serialization uses Base64 encoding of the binary payload
- Configurable `MessagePackSerializerOptions`

## Dependencies

- **Birko.Serialization** — Core serialization interfaces
- **MessagePack** — NuGet package (must be referenced by consuming project)

## Usage

```csharp
ISerializer serializer = new MessagePackBinarySerializer();

// Byte array (native format — most efficient)
byte[] bytes = serializer.SerializeToBytes(myObject);
var result = serializer.DeserializeFromBytes<MyType>(bytes);
```

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
