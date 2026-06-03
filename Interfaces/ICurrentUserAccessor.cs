using EBookDashboard.Models;

namespace EBookDashboard.Interfaces;

/// <summary>
/// Resolves the authenticated user's id from claims (authoritative) and keeps session in sync.
/// </summary>
public interface ICurrentUserAccessor
{
    /// <summary>Logged-in user id, or null when unauthenticated.</summary>
    int? GetUserId();

    /// <summary>Loads the user row for the current principal/session.</summary>
    Task<Users?> GetUserAsync(CancellationToken cancellationToken = default);
}
