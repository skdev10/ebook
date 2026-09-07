using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

public sealed class AiCoverQuotaService : IAiCoverQuotaService
{
    public const int DefaultLimit = 5;
    private readonly ApplicationDbContext _context;

    public AiCoverQuotaService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<AiCoverQuotaSnapshot> GetAsync(int userId, CancellationToken cancellationToken = default)
    {
        var row = await _context.Users.AsNoTracking()
            .Where(u => u.UserId == userId)
            .Select(u => new { u.AICoverGenerationsUsed, u.AICoverGenerationLimit })
            .FirstOrDefaultAsync(cancellationToken);
        return Map(row?.AICoverGenerationsUsed ?? 0, NormalizeLimit(row?.AICoverGenerationLimit ?? 0));
    }

    public async Task<AiCoverReserveResult> TryReserveAsync(int userId, CancellationToken cancellationToken = default)
    {
        await EnsureLimitAsync(userId, cancellationToken);
        var changed = await _context.Users
            .Where(u => u.UserId == userId && u.AICoverGenerationsUsed < u.AICoverGenerationLimit)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.AICoverGenerationsUsed, u => u.AICoverGenerationsUsed + 1)
                .SetProperty(u => u.UpdatedAt, DateTime.UtcNow), cancellationToken);
        var snap = await GetAsync(userId, cancellationToken);
        return new AiCoverReserveResult
        {
            Reserved = changed > 0,
            Snapshot = snap
        };
    }

    public async Task ReleaseAsync(int userId, CancellationToken cancellationToken = default)
    {
        await _context.Users
            .Where(u => u.UserId == userId && u.AICoverGenerationsUsed > 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.AICoverGenerationsUsed, u => u.AICoverGenerationsUsed - 1)
                .SetProperty(u => u.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task ResetAsync(int userId, CancellationToken cancellationToken = default)
    {
        await _context.Users
            .Where(u => u.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.AICoverGenerationsUsed, 0)
                .SetProperty(u => u.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    public async Task SetLimitAsync(int userId, int limit, CancellationToken cancellationToken = default)
    {
        var cap = Math.Max(1, limit);
        await _context.Users
            .Where(u => u.UserId == userId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.AICoverGenerationLimit, cap)
                .SetProperty(u => u.UpdatedAt, DateTime.UtcNow), cancellationToken);
    }

    private async Task EnsureLimitAsync(int userId, CancellationToken cancellationToken)
    {
        await _context.Users
            .Where(u => u.UserId == userId && u.AICoverGenerationLimit <= 0)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.AICoverGenerationLimit, DefaultLimit), cancellationToken);
    }

    private static int NormalizeLimit(int limit) => limit > 0 ? limit : DefaultLimit;

    private static AiCoverQuotaSnapshot Map(int used, int limit)
    {
        var safeUsed = Math.Max(0, used);
        var remaining = Math.Max(0, limit - safeUsed);
        return new AiCoverQuotaSnapshot
        {
            Used = safeUsed,
            Limit = limit,
            Remaining = remaining,
            LimitReached = remaining <= 0
        };
    }
}
