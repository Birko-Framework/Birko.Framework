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
    public List<IRule> Rules { get; set; } = [];

    public RuleSet(string name)
    {
        Name = name;
    }

    public RuleSet(string name, params IRule[] rules)
    {
        Name = name;
        Rules = new List<IRule>(rules);
    }
}
