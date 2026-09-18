namespace Birko.Rules;

/// <summary>
/// Provides field values for rule evaluation.
/// Abstracts the data source — could be a dictionary, an object, a DB row, a telemetry reading.
/// </summary>
public interface IRuleContext
{
    /// <summary>
    /// Try to get a field value by name. Returns false if field doesn't exist.
    /// </summary>
    bool TryGetValue(string field, out object? value);

    /// <summary>
    /// Check if a field exists in this context.
    /// </summary>
    bool HasField(string field);
}
