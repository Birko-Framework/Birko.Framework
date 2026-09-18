using System;

namespace Birko.Data.Patterns.Models;

/// <summary>
/// Provides the current user ID for audit tracking.
/// Implement this to integrate with your authentication system.
/// </summary>
public interface IAuditContext
{
    /// <summary>
    /// The current authenticated user's ID. Null if no user is authenticated.
    /// </summary>
    Guid? CurrentUserId { get; }
}
