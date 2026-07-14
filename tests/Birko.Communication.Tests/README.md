# Birko.Communication.Tests

Unit tests for the Birko.Communication base port surface (hardware-free).

## Running Tests

```bash
dotnet test
```

Covers `PortSettings.GetID`, the `AbstractPort` read-buffer helpers, the
Subscribe/Invoke/UnSubscribe event wiring, and `IPort : IDisposable` → `Close`,
via a trivial in-memory `AbstractPort` subclass. No OS ports are opened.

## License

Part of the Birko Framework.
