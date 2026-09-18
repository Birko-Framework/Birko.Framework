# Birko.Serialization.Newtonsoft

Newtonsoft.Json implementation of `ISerializer` for the Birko Framework.

## Features

- Implements `Birko.Serialization.ISerializer` using Newtonsoft.Json
- Configurable `JsonSerializerSettings` (defaults to camelCase, null-ignored, non-indented)
- Useful for interop with APIs or libraries requiring Newtonsoft-specific features

## Dependencies

- **Birko.Serialization** — Core serialization interfaces
- **Newtonsoft.Json** — NuGet package (must be referenced by consuming project)

## Usage

```csharp
ISerializer serializer = new NewtonsoftJsonSerializer();

string json = serializer.Serialize(myObject);
var result = serializer.Deserialize<MyType>(json);
```

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
