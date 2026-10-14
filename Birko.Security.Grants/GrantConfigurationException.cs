using System;

namespace Birko.Security.Grants;

/// <summary>
/// The grant configuration cannot express what was asked: an invalid role table, or a role, action or
/// resource type that was never declared. Always a programming or configuration error — never a "no".
/// </summary>
public class GrantConfigurationException : InvalidOperationException
{
    public GrantConfigurationException(string message) : base(message) { }
}
