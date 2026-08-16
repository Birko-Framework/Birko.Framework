---
area: serialization
generated-at: f3ac6755e788bc3e4693d27d37c583d67532a816
generated-on: 2026-07-30
sources:
  - ../Birko.Serialization.MessagePack/MessagePackBinarySerializer.cs
  - ../Birko.Serialization.Newtonsoft/NewtonsoftJsonSerializer.cs
  - ../Birko.Serialization.Protobuf/ProtobufBinarySerializer.cs
  - ../Birko.Serialization.Yaml/YamlDotNetSerializer.cs
  - ../Birko.Serialization/Core/ISerializer.cs
  - ../Birko.Serialization/Core/SerializationFormat.cs
  - ../Birko.Serialization/Json/SystemJsonSerializer.cs
  - ../Birko.Serialization/Xml/SystemXmlSerializer.cs
source-commits:   # sibling HEADs when this spec was last written (2026-07-30 10:48:14,
                  # commit 3728969). Reconstructed 2026-08-16 -- see .map.yml § BASELINE AMNESTY.
  ../Birko.Serialization: f852468
  ../Birko.Serialization.MessagePack: a52501a
  ../Birko.Serialization.Newtonsoft: befecef
  ../Birko.Serialization.Protobuf: 158da6c
  ../Birko.Serialization.Yaml: ee94017
shaped-by: []
---

# Serializer abstraction and format round-trip guarantees

## Purpose

`Birko.Serialization` defines one interface, `ISerializer`, that every serialization format in the
framework implements. It gives callers a single shape — serialize to a string, to a `byte[]`, or to a
`Stream`, synchronously or asynchronously, with a typed (`<T>`) or an untyped (`object` + `Type`)
overload for each — so that a component which needs to persist or transmit an object graph can be
handed a format at composition time rather than hard-coding one. Six implementations exist across five
sibling projects: `SystemJsonSerializer` and `SystemXmlSerializer` (in the dependency-free core
project), plus `NewtonsoftJsonSerializer`, `MessagePackBinarySerializer`, `ProtobufBinarySerializer`
and `YamlDotNetSerializer` in optional siblings that each pull in one third-party package. Consumers
include message-queue transports, job payload storage, workflow persistence and file-based stores.

The interface is a *shape* contract, not an equivalence contract. The implementations are each a thin
adapter over their underlying library and deliberately do not normalise that library's naming, null,
encoding, cancellation or error behaviour. This document records those divergences, because they are
observable and several of them break round-tripping *between overloads of the same serializer*.

## Requirements

### Requirement: Uniform serializer surface

The system SHALL expose every serialization format through `Birko.Serialization.ISerializer`, which
declares two identity members (`ContentType`, `Format`) and sixteen conversion members: `Serialize` /
`Deserialize` for `string`, `SerializeToBytes` / `DeserializeFromBytes` for `byte[]`, `Serialize` /
`Deserialize` for `Stream`, and `SerializeAsync` / `DeserializeAsync` for `Stream` — each of those
eight in both an untyped (`object` value, `Type` target) and a generic (`T`) form. All four
deserialization return shapes SHALL be nullable (`object?`, `T?`).

#### Scenario: Every implementation covers the whole surface

- **Given** the six concrete types `SystemJsonSerializer`, `SystemXmlSerializer`, `NewtonsoftJsonSerializer`, `MessagePackBinarySerializer`, `ProtobufBinarySerializer`, `YamlDotNetSerializer`
- **When** each is assigned to an `ISerializer` variable
- **Then** it compiles, because each declares all eighteen interface members with no explicit-interface or `NotSupportedException` stubs

#### Scenario: A decoded null is a legitimate result, not an error

- **Given** any implementation and a payload that decodes to no value (for example the JSON text `null`)
- **When** `Deserialize<T>(data)` is called
- **Then** no implementation contains a throw for that case; the declared return type is `T?` and the caller must null-check

### Requirement: Format and content-type identification

The system SHALL report a fixed `SerializationFormat` and a fixed `ContentType` string per
implementation: `SystemJsonSerializer` → `Json` / `application/json`; `NewtonsoftJsonSerializer` →
`Json` / `application/json`; `MessagePackBinarySerializer` → `MessagePack` /
`application/x-msgpack`; `ProtobufBinarySerializer` → `Protobuf` / `application/x-protobuf`;
`SystemXmlSerializer` → `Xml` / `application/xml`; `YamlDotNetSerializer` → `Yaml` /
`application/yaml`. `SerializationFormat` SHALL enumerate exactly `Json`, `MessagePack`, `Protobuf`,
`Xml`, `Yaml`.

#### Scenario: Content type is a constant expression-bodied property

- **Given** an instance of `MessagePackBinarySerializer`
- **When** `ContentType` is read twice, with different constructor options in between
- **Then** both reads return `"application/x-msgpack"` — the value is not derived from configuration

#### Scenario: Format cannot distinguish the two JSON implementations

- **Given** a `SystemJsonSerializer` and a `NewtonsoftJsonSerializer`
- **When** `Format` and `ContentType` are compared
- **Then** both report `SerializationFormat.Json` and `"application/json"`, so a caller selecting a serializer by `Format` cannot tell which JSON stack — or which wire dialect (see "Null-valued properties") — it will get

### Requirement: Null arguments are rejected before any work

The system SHALL reject `null` for `value`, `data`, `type` and `stream` with
`ArgumentNullException`, via `ArgumentNullException.ThrowIfNull`, on every conversion member. A
`null` object graph therefore SHALL NOT be serializable to a format-level null literal (JSON `null`,
MessagePack nil, an empty XML element) through any implementation.

#### Scenario: Serializing null throws instead of emitting a null literal

- **Given** any of the six implementations
- **When** `Serialize<string>(null!)` is called
- **Then** `ArgumentNullException` is thrown; no implementation has a branch that emits a format null

#### Scenario: Delegating byte overloads still guard

- **Given** a `NewtonsoftJsonSerializer` or `YamlDotNetSerializer`
- **When** `SerializeToBytes(null!)` is called — a member that has no `ThrowIfNull` of its own
- **Then** `ArgumentNullException` is still thrown, because the body's first act is to call `Serialize(value)` / `Serialize<T>(value)`, which does guard

#### Scenario: Value types are never rejected

- **Given** a `SystemJsonSerializer`
- **When** `Serialize<int>(0)` is called
- **Then** `ArgumentNullException.ThrowIfNull(value)` cannot trip on a boxed non-null struct, and serialization proceeds

### Requirement: Binary formats expose their string overloads as Base64

The system SHALL implement the `string`-returning members of `MessagePackBinarySerializer` and
`ProtobufBinarySerializer` as `Convert.ToBase64String` over the binary payload, and their
`string`-consuming members as `Convert.FromBase64String` followed by binary decoding. The text formats
(`SystemJsonSerializer`, `NewtonsoftJsonSerializer`, `SystemXmlSerializer`, `YamlDotNetSerializer`)
SHALL return their native text form from the same members.

#### Scenario: MessagePack string round-trip goes via Base64

- **Given** a `MessagePackBinarySerializer` and a POCO
- **When** `Serialize(poco)` is called
- **Then** the returned string is the Base64 encoding of `MessagePackSerializer.Serialize(...)` output, and `Deserialize<T>(thatString)` decodes it back

#### Scenario: Malformed Base64 surfaces as FormatException, not a serialization error

- **Given** a `ProtobufBinarySerializer`
- **When** `Deserialize<T>("not base64!")` is called
- **Then** `Convert.FromBase64String` throws `FormatException` before protobuf-net is reached — the failure is not a protobuf error type

### Requirement: Per-implementation default configuration, replaceable wholesale

The system SHALL apply these defaults when the constructor's optional configuration argument is
omitted, and SHALL use a caller-supplied argument verbatim with no merging, no partial override and no
re-application of the defaults:

- `SystemJsonSerializer`: `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = false }` and `JsonWriterOptions { Indented = false }`
- `NewtonsoftJsonSerializer`: `JsonSerializerSettings { ContractResolver = CamelCasePropertyNamesContractResolver, NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.None }`
- `MessagePackBinarySerializer`: `MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance)`
- `YamlDotNetSerializer`: a `SerializerBuilder` with `CamelCaseNamingConvention`, and a `DeserializerBuilder` with `CamelCaseNamingConvention` plus `IgnoreUnmatchedProperties()`
- `SystemXmlSerializer`: `XmlWriterSettings { Indent = false, OmitXmlDeclaration = false, Encoding = new UTF8Encoding(false) }` and `XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null }`
- `ProtobufBinarySerializer`: **no configuration at all** — the type has no constructor and no fields

#### Scenario: Supplied XML reader settings discard the hardening

- **Given** a caller who wants indented XML and constructs `new SystemXmlSerializer(new XmlWriterSettings { Indent = true }, new XmlReaderSettings())`
- **When** any `Deserialize` overload runs
- **Then** the reader uses the caller's settings object; the `DtdProcessing = Prohibit` / `XmlResolver = null` assignments in the constructor's `??` fallback never execute

#### Scenario: Protobuf offers no per-instance configuration

- **Given** two `ProtobufBinarySerializer` instances
- **When** each serializes the same value
- **Then** the results are identical, because both call the static `ProtoBuf.Serializer` facade; the only way to influence behaviour is to mutate protobuf-net's process-global default type model

#### Scenario: YAML ignores keys with no matching member

- **Given** a default-constructed `YamlDotNetSerializer` and YAML text carrying a key not present on `T`
- **When** `Deserialize<T>(yaml)` is called
- **Then** the extra key is ignored, because the default deserializer is built with `IgnoreUnmatchedProperties()`; no other implementation configures an equivalent, so their behaviour follows their own library's default and is not normalised

### Requirement: Null-valued properties are dropped by Newtonsoft and kept by System.Text.Json

The system SHALL, in its default `NewtonsoftJsonSerializer` configuration, omit null-valued properties
from the output (`NullValueHandling.Ignore`), and SHALL NOT configure any equivalent suppression in
`SystemJsonSerializer`. The two implementations therefore SHALL produce different JSON for the same
object while advertising the same `Format` and `ContentType`.

#### Scenario: The same POCO yields different JSON

- **Given** a POCO with a non-null `Name` and a null `Description`
- **When** it is serialized by a default `NewtonsoftJsonSerializer` and by a default `SystemJsonSerializer`
- **Then** the Newtonsoft output contains no `description` member, while the System.Text.Json output contains `"description":null`

### Requirement: XML declaration encoding agrees across all overloads

The system SHALL emit an XML declaration on every `SystemXmlSerializer` write path
(`OmitXmlDeclaration = false`), and the declared encoding SHALL be the same for the string overloads
as for the `byte[]` and `Stream` overloads. To achieve this the string overloads SHALL write through
the private `EncodedStringWriter`, a `StringWriter` subclass whose `Encoding` property returns
`_writerSettings.Encoding` rather than `StringWriter`'s inherent UTF-16.

#### Scenario: String and byte output declare the same encoding

- **Given** a default-constructed `SystemXmlSerializer`
- **When** `Serialize(value)` and `SerializeToBytes(value)` are both called
- **Then** both payloads begin with an `<?xml … encoding="utf-8"?>` declaration; without `EncodedStringWriter` the string overload would have declared `utf-16` while the bytes declared `utf-8`

#### Scenario: Byte output carries no byte-order mark

- **Given** a default-constructed `SystemXmlSerializer` whose writer encoding is `new UTF8Encoding(false)`
- **When** `SerializeToBytes(value)` is called
- **Then** the returned array starts at `<` (`0x3C`), with no `EF BB BF` preamble

### Requirement: XML reads are hardened against DTD and external entities by default

The system SHALL create every `SystemXmlSerializer` read path's `XmlReader` from
`_readerSettings`, whose default value sets `DtdProcessing = DtdProcessing.Prohibit` and
`XmlResolver = null`. This SHALL apply uniformly to the string, `byte[]`, `Stream` and async
deserialize overloads.

#### Scenario: A document with a DOCTYPE is refused

- **Given** a default-constructed `SystemXmlSerializer` and XML text containing a `<!DOCTYPE …>` declaration
- **When** `Deserialize<T>(xml)` is called
- **Then** the read fails rather than processing the DTD, because `DtdProcessing.Prohibit` is in force

### Requirement: A fresh XmlSerializer is constructed on every XML call

The system SHALL construct `new XmlSerializer(...)` inside each of the twelve `SystemXmlSerializer`
conversion members; it SHALL NOT hold or populate any serializer cache field.

#### Scenario: No cached serializer state exists

- **Given** the `SystemXmlSerializer` type
- **When** its fields are inspected
- **Then** only `_writerSettings` and `_readerSettings` exist; every conversion member begins by constructing a new `XmlSerializer` for `value.GetType()` or `typeof(T)`

### Requirement: Untyped overloads serialize by runtime type; generic overloads by static type argument

The system SHALL pass the runtime type to the underlying library in every untyped write overload —
`value.GetType()` for `SystemJsonSerializer`, `NewtonsoftJsonSerializer`, `MessagePackBinarySerializer`,
`YamlDotNetSerializer` and `SystemXmlSerializer` — and SHALL NOT pass any explicit type in the generic
write overloads, so those bind to `typeof(T)`. `Serialize<TBase>(derived)` and
`Serialize((object)derived)` are therefore NOT equivalent.

#### Scenario: XML builds its serializer from a different type per overload

- **Given** `class Derived : Base` and a `Derived` instance
- **When** `Serialize<Base>(instance)` is called
- **Then** `new XmlSerializer(typeof(Base))` is constructed — it does not know about `Derived` unless `Base` carries `[XmlInclude(typeof(Derived))]`
- **And** `Serialize((object)instance)` instead constructs `new XmlSerializer(typeof(Derived))` and emits the derived shape

#### Scenario: MessagePack passes the runtime type only on the untyped path

- **Given** a `MessagePackBinarySerializer`
- **When** `SerializeToBytes(object value)` runs
- **Then** it calls `MessagePackSerializer.Serialize(value.GetType(), value, _options)`
- **And** `SerializeToBytes<T>(T value)` calls `MessagePackSerializer.Serialize(value, _options)`, supplying no runtime type

### Requirement: JsonWriterOptions govern exactly one System.Text.Json overload

The system SHALL use `_writerOptions` (a `JsonWriterOptions`) in `SystemJsonSerializer` only inside
`Serialize<T>(Stream stream, T value)`, which wraps the stream in a `Utf8JsonWriter(stream,
_writerOptions)`. Every other member SHALL take its formatting from `_options`
(`JsonSerializerOptions`). Indentation of the generic stream overload is therefore governed by
`_writerOptions.Indented` and indentation everywhere else by `_options.WriteIndented`.

#### Scenario: Indentation diverges between the two stream overloads

- **Given** `new SystemJsonSerializer(new JsonSerializerOptions { WriteIndented = true })` — `writerOptions` omitted, so `_writerOptions.Indented` defaults to `false`
- **When** the same value is written by `Serialize(stream, (object)value)` and by `Serialize<T>(stream, value)`
- **Then** the untyped overload writes indented JSON and the generic overload writes compact JSON

#### Scenario: Writer options are ignored by the string and byte overloads

- **Given** `new SystemJsonSerializer(null, new JsonWriterOptions { Indented = true })`
- **When** `Serialize(value)` or `SerializeToBytes(value)` is called
- **Then** the output is compact, because those members pass only `_options`, whose default `WriteIndented` is `false`

### Requirement: Cancellation observation differs per implementation

The system SHALL observe `CancellationToken` on the async members as follows, and SHALL NOT normalise
the difference:

- `SystemJsonSerializer`: the token is forwarded to `JsonSerializer.SerializeAsync` /
  `DeserializeAsync` on all four members — cancellation can interrupt work in progress.
- `MessagePackBinarySerializer`: the token is forwarded to `MessagePackSerializer.SerializeAsync` /
  `DeserializeAsync` on all four members.
- `SystemXmlSerializer`, `ProtobufBinarySerializer`, `YamlDotNetSerializer`: no async library API
  exists, so each of the four members calls `cancellationToken.ThrowIfCancellationRequested()` once
  before doing the work synchronously and returning `Task.CompletedTask` / `Task.FromResult(...)`. A
  token cancelled *after* that check has no effect.
- `NewtonsoftJsonSerializer`: asymmetric. `SerializeAsync` / `SerializeAsync<T>` call
  `ThrowIfCancellationRequested()` up front and then `await jsonWriter.FlushAsync(cancellationToken)`.
  `DeserializeAsync` / `DeserializeAsync<T>` have **no** up-front check and instead run the whole
  deserialize inside `Task.Run(() => …, cancellationToken)`.

#### Scenario: Pre-cancelled token stops the XML writer before any bytes are emitted

- **Given** a `SystemXmlSerializer` and a `CancellationToken` that is already cancelled
- **When** `SerializeAsync(stream, value, token)` is invoked
- **Then** `OperationCanceledException` propagates and the stream is untouched

#### Scenario: Cancelling mid-write is ignored by the sync-wrapped implementations

- **Given** a `YamlDotNetSerializer` and a token cancelled immediately after `SerializeAsync` begins
- **When** the serialization proceeds
- **Then** the write completes in full; the only token check happened before the work started

#### Scenario: Newtonsoft deserialization offloads to the thread pool and only cancels scheduling

- **Given** a `NewtonsoftJsonSerializer` and a pre-cancelled token
- **When** `DeserializeAsync<T>(stream, token)` is awaited
- **Then** the `StreamReader` and `JsonTextReader` are still constructed on the calling thread, `Task.Run` never invokes the delegate, and the awaited task surfaces `TaskCanceledException`
- **And** for a token cancelled after the delegate has started, the in-flight `serializer.Deserialize` call runs to completion on a pool thread — the token cannot interrupt it

### Requirement: Async argument-validation failures are synchronous for some implementations and deferred for others

The system SHALL surface argument-validation exceptions from the `*Async` members according to whether
the member is declared `async`. `SystemJsonSerializer` (all four), `NewtonsoftJsonSerializer` (all
four) and `MessagePackBinarySerializer` (`DeserializeAsync`, `DeserializeAsync<T>`) are `async`, so
their `ArgumentNullException` and `OperationCanceledException` SHALL be captured in the returned
`Task`. `SystemXmlSerializer` (all four), `ProtobufBinarySerializer` (all four),
`YamlDotNetSerializer` (all four) and `MessagePackBinarySerializer` (`SerializeAsync`,
`SerializeAsync<T>`) are plain `Task`-returning methods, so their exceptions SHALL be thrown
synchronously at the call site.

#### Scenario: The same misuse throws at different moments

- **Given** a null stream
- **When** `xmlSerializer.SerializeAsync(null!, value)` is called without awaiting
- **Then** `ArgumentNullException` is thrown by the call itself and no `Task` is produced
- **And** `jsonSerializer.SerializeAsync(null!, value)` returns a `Task` that only faults with `ArgumentNullException` when awaited

#### Scenario: MessagePack is internally inconsistent

- **Given** a `MessagePackBinarySerializer` and a null stream
- **When** `SerializeAsync(null!, value)` is called
- **Then** `ArgumentNullException` is thrown synchronously (the member is not `async`)
- **And** `DeserializeAsync<T>(null!)` instead returns a faulted `Task` (the member is `async`)

### Requirement: The caller's stream is not taken over

The system SHALL NOT close or dispose a `Stream` passed by the caller. `NewtonsoftJsonSerializer` and
`YamlDotNetSerializer` SHALL construct their `StreamWriter` / `StreamReader` with `leaveOpen: true`.
`SystemJsonSerializer` SHALL pass the stream to System.Text.Json or wrap it in a `Utf8JsonWriter`,
neither of which owns it. `MessagePackBinarySerializer` and `ProtobufBinarySerializer` SHALL hand the
stream straight to their library. `SystemXmlSerializer` SHALL create its `XmlWriter` / `XmlReader`
from `_writerSettings` / `_readerSettings`, neither of which sets `CloseOutput` / `CloseInput` in the
constructor defaults — so stream ownership follows whatever settings object is in use.

#### Scenario: A stream survives consecutive writes

- **Given** an open `MemoryStream` and a `NewtonsoftJsonSerializer`
- **When** `Serialize(stream, a)` is called and then `Serialize(stream, b)`
- **Then** the second call succeeds, because the first `StreamWriter` was created with `leaveOpen: true`

#### Scenario: Caller-supplied XML settings can close the stream

- **Given** `new SystemXmlSerializer(new XmlWriterSettings { CloseOutput = true })`
- **When** `Serialize(stream, value)` returns and its `using var xmlWriter` is disposed
- **Then** the caller's stream is closed — the implementation does not defend against this

### Requirement: Newtonsoft and YAML stream output carries a UTF-8 preamble that their byte output does not

The system SHALL write `NewtonsoftJsonSerializer` and `YamlDotNetSerializer` stream payloads through
`new StreamWriter(stream, Encoding.UTF8, …)` — the preamble-bearing static `Encoding.UTF8` — while
producing their `byte[]` payloads with `Encoding.UTF8.GetBytes(text)`, which emits no preamble. On the
read side the stream overloads SHALL use `detectEncodingFromByteOrderMarks: true` while
`DeserializeFromBytes` SHALL use `Encoding.UTF8.GetString(data)`, which does not strip a byte-order
mark. `SystemXmlSerializer` SHALL instead use `new UTF8Encoding(false)` and `SystemJsonSerializer`
SHALL introduce no `TextWriter` at all.

#### Scenario: Stream bytes and byte-array bytes differ for the same value

- **Given** a `YamlDotNetSerializer` and a value
- **When** `SerializeToBytes(value)` is compared with the contents of a `MemoryStream` written by `Serialize(stream, value)`
- **Then** the stream contents are three bytes longer, prefixed with `EF BB BF`

#### Scenario: Cross-overload transfer fails on the byte path

- **Given** the bytes produced by `NewtonsoftJsonSerializer.Serialize(stream, value)`
- **When** they are handed to `DeserializeFromBytes<T>(bytes)`
- **Then** the byte-order mark is decoded to a leading U+FEFF character by `Encoding.UTF8.GetString`, which is not stripped, and JSON parsing fails
- **And** the same bytes handed to `Deserialize<T>(stream)` succeed, because that path passes `detectEncodingFromByteOrderMarks: true`

### Requirement: Text formats go through a string on the byte path; binary and System.Text.Json do not

The system SHALL implement `NewtonsoftJsonSerializer` and `YamlDotNetSerializer` byte members as
text-then-UTF-8 conversions (`Serialize(value)` → `Encoding.UTF8.GetBytes`, and
`Encoding.UTF8.GetString` → `Deserialize(text, …)`). `SystemJsonSerializer` SHALL use
System.Text.Json's native UTF-8 byte APIs (`SerializeToUtf8Bytes`, `Deserialize(byte[], …)`).
`SystemXmlSerializer` SHALL use a `MemoryStream` with `XmlWriter` / `XmlReader`.
`MessagePackBinarySerializer` and `ProtobufBinarySerializer` SHALL operate on bytes natively.

#### Scenario: The intermediate string is observable in the exception path

- **Given** a `YamlDotNetSerializer` and a `byte[]` that is not valid UTF-8
- **When** `DeserializeFromBytes<T>(bytes)` is called
- **Then** `Encoding.UTF8.GetString` substitutes replacement characters (it does not throw) and the failure appears later, as a YAML parse error over the corrupted text

### Requirement: MessagePack streams without an intermediate copy and flows the token through the work

The system SHALL use MessagePack's native `(Type|T, Stream, options[, CancellationToken])` overloads
for all four stream members and both async pairs, so no intermediate `byte[]` or `MemoryStream` copy
of the payload is made and the cancellation token reaches the actual serialize/deserialize work.

#### Scenario: No buffering layer exists between the caller's stream and MessagePack

- **Given** a `MessagePackBinarySerializer` and a `Stream`
- **When** `Serialize(stream, (object)value)` is called
- **Then** the body is the single call `MessagePackSerializer.Serialize(value.GetType(), stream, value, _options)` — no `MemoryStream`, no `ToArray()`, no `CopyTo`

### Requirement: MessagePack defaults are contractless and treat the payload as trusted

The system SHALL default `MessagePackBinarySerializer` to
`MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance)`, meaning
target types need no `[MessagePackObject]` / `[Key]` annotations. It SHALL NOT apply
`.WithSecurity(MessagePackSecurity.UntrustedData)` and SHALL NOT apply any LZ4 compression option.

#### Scenario: An unannotated POCO round-trips

- **Given** a plain public class with public properties and no MessagePack attributes
- **When** it is passed to `SerializeToBytes` and then `DeserializeFromBytes<T>`
- **Then** the round-trip succeeds, because the contractless resolver maps members by name

#### Scenario: No hardening is applied for hostile input

- **Given** the `MessagePackBinarySerializer` constructor with `options` omitted
- **When** the constructed `_options` is inspected
- **Then** it is `MessagePackSerializerOptions.Standard` with only the resolver replaced; there is no `WithSecurity` call anywhere in the file

### Requirement: The Protobuf serializer is stateless and process-global

The system SHALL implement `ProtobufBinarySerializer` with no fields and no constructor, dispatching
every member to the static `ProtoBuf.Serializer` facade.

#### Scenario: Configuration is not per-instance

- **Given** two independently constructed `ProtobufBinarySerializer` instances
- **When** the same type is serialized by each
- **Then** the output is identical, because both resolve their type model from protobuf-net's process-global default

#### Scenario: Unannotated types are not supported

- **Given** a POCO with no `[ProtoContract]` / `[ProtoMember]` attributes
- **When** it is serialized
- **Then** the failure comes from protobuf-net's own type-model resolution; the implementation adds no fallback resolver of its own (unlike MessagePack's contractless default)

### Requirement: The Protobuf write path passes no type; the read path passes the caller's Type

The system SHALL call `Serializer.Deserialize(type, stream)` in all four untyped read overloads,
supplying the caller's `Type` explicitly, but SHALL call `Serializer.Serialize(stream, value)` in the
untyped write overloads, supplying no type argument — so protobuf-net's generic method binds its type
parameter to the declared static type `object`, not to `value.GetType()`.

#### Scenario: Read and write are asymmetric about the runtime type

- **Given** a `ProtobufBinarySerializer` and an `object`-typed reference to a `[ProtoContract]` instance
- **When** `SerializeToBytes(object value)` is compared with `DeserializeFromBytes(byte[] data, Type type)`
- **Then** the read overload names the type (`Serializer.Deserialize(type, stream)`) while the write overload does not (`Serializer.Serialize(stream, value)`), so whether the runtime type is honoured on the write side depends entirely on protobuf-net's handling of a generic type parameter bound to `object`

### Requirement: Newtonsoft is the only implementation that moves work to the thread pool

The system SHALL run all serialization and deserialization on the calling thread, or via the
underlying library's own async I/O, in every implementation except
`NewtonsoftJsonSerializer.DeserializeAsync` / `DeserializeAsync<T>`, which SHALL wrap the deserialize
call in `Task.Run(…, cancellationToken)`.

#### Scenario: A custom converter observes a different thread under Newtonsoft

- **Given** a `NewtonsoftJsonSerializer` with a `JsonConverter` that inspects the current thread
- **When** `DeserializeAsync<T>(stream)` is awaited
- **Then** the converter executes on a thread-pool thread, while the same converter under `Deserialize<T>(stream)` executes on the caller's thread

### Requirement: Serializer instances hold only immutable configuration

The system SHALL declare every implementation's configuration fields `readonly` and SHALL NOT keep
per-call mutable state on the instance: `SystemXmlSerializer` constructs a new `XmlSerializer` per
call, `NewtonsoftJsonSerializer` constructs a new `JsonSerializer` per stream call,
`SystemJsonSerializer` constructs a new `Utf8JsonWriter` per generic stream write, and
`ProtobufBinarySerializer` holds nothing.

#### Scenario: One instance can serve concurrent callers

- **Given** a single `SystemJsonSerializer` shared by several threads
- **When** each thread calls `SerializeToBytes` concurrently
- **Then** no instance state is mutated — the only fields are the two readonly options objects, which are passed by reference to System.Text.Json
