using System.Security.Claims;
using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

/// <summary>
/// Single source of truth for the logged-in user: <see cref="ClaimTypes.NameIdentifier"/> wins over session.
/// </summary>
public sealed class CurrentUserAccessor : ICurrentUserAccessor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CurrentUserAccessor> _logger;

    public CurrentUserAccessor(
        IHttpContextAccessor httpContextAccessor,
        ApplicationDbContext context,
        ILogger<CurrentUserAccessor> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _context = context;
        _logger = logger;
    }

    /// <inheritdoc />
    public int? GetUserId()
    {
        var http = _httpContextAccessor.HttpContext;
        if (http == null)
            return null;

        int? claimUserId = null;
        var idRaw = http.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (int.TryParse(idRaw, out var parsedClaim) && parsedClaim > 0)
            claimUserId = parsedClaim;

        int? sessionUserId = null;
        try
        {
            sessionUserId = http.Session.GetInt32("UserId");
        }
        catch
        {
            // Session store unavailable; claims-only.
        }

        if (claimUserId.HasValue)
        {
            if (sessionUserId.HasValue && sessionUserId.Value != claimUserId.Value)
            {
                _logger.LogWarning(
                    "Session UserId {SessionUserId} did not match claim user id {ClaimUserId}; using claim.",
                    sessionUserId.Value,
                    claimUserId.Value);
            }

            try
            {
                if (!sessionUserId.HasValue || sessionUserId.Value != claimUserId.Value)
                    http.Session.SetInt32("UserId", claimUserId.Value);
            }
            catch
            {
                // Non-fatal when session is unavailable.
            }

            return claimUserId.Value;
        }

        if (sessionUserId.HasValue && sessionUserId.Value > 0)
            return sessionUserId.Value;

        return null;
    }

    /// <inheritdoc />
    public async Task<Users?> GetUserAsync(CancellationToken cancellationToken = default)
    {
        var userId = GetUserId();
        if (!userId.HasValue)
            return null;

        return await _context.Users
            .AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId.Value, cancellationToken);
    }
}
