# Birko.Serialization.Yaml

YamlDotNet implementation of `ISerializer` for the Birko Framework.

## Features

- Implements `Birko.Serialization.ISerializer` using YamlDotNet
- Configurable `YamlDotNet.Serialization.ISerializer` / `IDeserializer`
- Defaults: camelCase naming, `IgnoreUnmatchedProperties()` on deserialize
- `ContentType` = `application/yaml`; `Format` = `SerializationFormat.Yaml`
- Byte serialization via UTF-8 encoding of YAML text

## Dependencies

- **Birko.Serialization** — Core serialization interfaces
- **YamlDotNet** — NuGet package (must be referenced by consuming project)

## Usage

```csharp
ISerializer serializer = new YamlDotNetSerializer();

string yaml = serializer.Serialize(myObject);
var result = serializer.Deserialize<MyType>(yaml);
```

Custom YamlDotNet pipeline (e.g., underscored naming, no camelCase):

```csharp
var yamlSer = new SerializerBuilder()
    .WithNamingConvention(UnderscoredNamingConvention.Instance)
    .Build();
var yamlDeser = new DeserializerBuilder()
    .WithNamingConvention(UnderscoredNamingConvention.Instance)
    .Build();

ISerializer serializer = new YamlDotNetSerializer(yamlSer, yamlDeser);
```

## License

This project is licensed under the MIT License - see the [License.md](License.md) file for details.
