using Birko.Data.Models;

namespace Birko.Data.InMemory.Tests;

/// <summary>
/// Simple entity used across the in-memory store tests.
/// </summary>
public class TestModel : AbstractModel
{
    public string Name { get; set; } = string.Empty;
    public int Value { get; set; }
}
