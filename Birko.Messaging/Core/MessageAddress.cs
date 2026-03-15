using System;

namespace Birko.Messaging;

public sealed class MessageAddress
{
    public string Value { get; }
    public string? DisplayName { get; }

    public MessageAddress(string value, string? displayName = null)
    {
        Value = value ?? throw new ArgumentNullException(nameof(value));
        DisplayName = displayName;
    }

    public override string ToString() =>
        string.IsNullOrEmpty(DisplayName) ? Value : $"{DisplayName} <{Value}>";

    public override bool Equals(object? obj) =>
        obj is MessageAddress other && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() =>
        StringComparer.OrdinalIgnoreCase.GetHashCode(Value);
}
