using System;

namespace Birko.Security.Grants;

/// <summary>
/// A grant write was refused by an authorization rule. <see cref="Code"/> is one of <see cref="GrantErrorCodes"/>,
/// so a host can map it to a response without parsing the message.
/// </summary>
public class GrantRefusedException : Exception
{
    /// <summary>One of <see cref="GrantErrorCodes"/>.</summary>
    public string Code { get; }

    public GrantRefusedException(string code, string message) : base(message)
    {
        Code = code;
    }
}
