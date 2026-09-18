# Birko.Serialization.Protobuf

Protocol Buffers binary serialization implementation of `ISerializer` for the Birko Framework.

## Features

- Efficient binary format for strongly-typed data transfer
- String serialization uses Base64 encoding of the binary payload
- Types require `[ProtoContract]` / `[ProtoMember]` attributes

## Dependencies

- **Birko.Serialization** — Core serialization interfaces
- **protobuf-net** — NuGet package (must be referenced by consuming project)

## Usage

```csharp
ISerializer serializer = new ProtobufBinarySerializer();

byte[] bytes = serializer.SerializeToBytes(myObject);
var result = serializer.DeserializeFromBytes<MyType>(bytes);
```

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
