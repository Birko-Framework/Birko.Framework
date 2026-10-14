using System;
using System.Collections.Generic;

namespace Birko.Security.Grants;

/// <summary>Configuration for sign-in: who the bootstrap admins are, and how long an invitation lives.</summary>
public class GrantSignInOptions
{
    /// <summary>
    /// External identities that become global admins on sign-in, as <c>provider:subject</c> — e.g. <c>github:4821337</c>.
    /// The provider part compares case-insensitively, the subject exactly. Never an email, and there is no
    /// "first user to sign up" path: the first admin is whoever this list names.
    /// </summary>
    public IList<string> BootstrapAdmins { get; } = new List<string>();

    /// <summary>How long a new invitation stays redeemable. Default 7 days.</summary>
    public TimeSpan InvitationLifetime { get; set; } = TimeSpan.FromDays(7);
}
