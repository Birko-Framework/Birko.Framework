using System;
using System.Collections.Generic;

namespace Birko.Rules;

/// <summary>
/// Named, reusable collection of rules with enabled/disabled toggle.
/// Example: "IoT Temperature Alarms", "Warehouse Stock Rules".
/// </summary>
public class RuleSet
{
    public string Name { get; set; }
    public string? Description { get; set; }
    public bool IsEnabled { get; set; } = true;
    /// <remarks>
    /// SH-M370 (TASK-156): a <see cref="RuleList"/>, safe to edit while requests evaluate the set. Assigning a list copies
    /// it, so later edits go through this property, not through the caller's original list.
    /// </remarks>
    public RuleList Rules
    {
        get => _rules;
        // TASK-651: the remark promised a copy and the setter stored the caller's list, so its later edits changed the set.
        set => _rules = new RuleList(value ?? throw new ArgumentNullException(nameof(value)));
    }

    private RuleList _rules = new();

    public RuleSet(string name)
    {
        Name = name;
    }

    public RuleSet(string name, params IRule[] rules)
    {
        Name = name;
        Rules = new RuleList(rules);
    }
}
