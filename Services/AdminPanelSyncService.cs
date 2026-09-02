using EBookDashboard.Interfaces;
using EBookDashboard.Models;
using EBookDashboard.Models.ViewModels;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services;

/// <summary>Loads the same user-panel facts (flow, covers, formatting, billing) for admin screens.</summary>
public sealed class AdminPanelSyncService : IAdminPanelSyncService
{
    private readonly ApplicationDbContext _db;
    private readonly BookFlowStateService _flow;

    public AdminPanelSyncService(ApplicationDbContext db, BookFlowStateService flow)
    {
        _db = db;
        _flow = flow;
    }

    /// <inheritdoc />
    public async Task<AdminUserWorkspaceViewModel?> GetUserWorkspaceAsync(int userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users.AsNoTracking()
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == userId, cancellationToken);
        if (user == null) return null;

        var books = await _db.Books.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.UpdatedAt ?? b.CreatedAt)
            .ToListAsync(cancellationToken);

        var bookIds = books.Select(b => b.BookId).ToList();
        var chapterCounts = bookIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.Chapters.AsNoTracking()
                .Where(c => bookIds.Contains(c.BookId))
                .GroupBy(c => c.BookId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        var coverBookIds = bookIds.Count == 0
            ? new HashSet<int>()
            : (await _db.BookCoverDesigns.AsNoTracking()
                .Where(c => bookIds.Contains(c.BookId) && !c.IsDeleted)
                .Select(c => c.BookId)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();

        var formattingBookIds = bookIds.Count == 0
            ? new HashSet<int>()
            : (await _db.BookFormatting.AsNoTracking()
                .Where(f => bookIds.Contains(f.BookId))
                .Select(f => f.BookId)
                .Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();

        var summaries = new List<AdminBookWorkspaceSummary>(books.Count);
        foreach (var b in books)
        {
            var (step, path) = await _flow.GetResumeStepAsync(b.BookId, cancellationToken);
            summaries.Add(new AdminBookWorkspaceSummary
            {
                BookId = b.BookId,
                Title = string.IsNullOrWhiteSpace(b.Title) ? "Untitled" : b.Title,
                Status = b.Status ?? "",
                Genre = b.Genre ?? "",
                WordCount = b.WordCount,
                ChapterCount = chapterCounts.GetValueOrDefault(b.BookId),
                FlowStep = step,
                FlowLabel = BookFlowStateService.StepToLabel(step),
                FlowPercent = BookFlowStateService.IsPublishedStatus(b.Status) ? 100 : BookFlowStateService.StepToPercent(step),
                FlowPath = path,
                HasCover = coverBookIds.Contains(b.BookId) || !string.IsNullOrWhiteSpace(b.CoverImagePath),
                HasFormatting = formattingBookIds.Contains(b.BookId),
                CoverImagePath = b.CoverImagePath,
                CreatedAt = b.CreatedAt,
                UpdatedAt = b.UpdatedAt
            });
        }

        var plan = await _db.AuthorPlans.AsNoTracking()
            .Where(p => p.UserId == userId)
            .OrderByDescending(p => p.IsActive == 1)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var payments = await _db.AuthorBills.AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.CreatedAt)
            .Take(25)
            .Select(b => new AdminPaymentRow
            {
                BillId = b.BillId,
                Description = b.Description ?? "",
                Amount = b.TotalAmount,
                Currency = b.Currency ?? "usd",
                Status = b.Status,
                PaymentReference = b.PaymentReference,
                CreatedAt = b.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var notifications = await _db.Notifications.AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Take(20)
            .Select(n => new AdminNotificationRow
            {
                NotificationId = n.NotificationId,
                Title = n.Title,
                Message = n.Message,
                Type = n.Type,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync(cancellationToken);

        var coverCount = bookIds.Count == 0
            ? 0
            : await _db.BookCoverDesigns.AsNoTracking()
                .CountAsync(c => bookIds.Contains(c.BookId) && !c.IsDeleted, cancellationToken);

        return new AdminUserWorkspaceViewModel
        {
            UserId = user.UserId,
            FullName = user.FullName ?? "",
            UserEmail = user.UserEmail ?? "",
            RoleName = user.Role?.RoleName ?? "Unknown",
            Status = user.Status ?? "",
            SignupMethod = string.IsNullOrEmpty(user.Password) ? "OAuth" : "Manual",
            ProfilePicturePath = user.ProfilePicturePath,
            CreatedAt = user.CreatedAt,
            LastLoginAt = user.LastLoginAt,
            ProfileCompletionPercentage = CalculateProfileCompletion(user),
            HasCompletedTour = user.HasCompletedTour == true,
            PlanName = plan?.PlanName,
            PlanRate = plan?.PlanRate,
            PlanStart = plan?.StartDate,
            PlanEnd = plan?.EndDate,
            PlanActive = plan?.IsActive == 1 && (plan.EndDate == default || plan.EndDate.Year < 1981 || plan.EndDate >= DateTime.UtcNow),
            BookCount = books.Count,
            PublishedCount = books.Count(b => BookFlowStateService.IsPublishedStatus(b.Status)),
            DraftCount = books.Count(b => !BookFlowStateService.IsPublishedStatus(b.Status)),
            ChapterCount = chapterCounts.Values.Sum(),
            CoverCount = coverCount,
            LifetimeBilled = payments.Sum(p => p.Amount),
            Books = summaries,
            Payments = payments,
            Notifications = notifications
        };
    }

    /// <inheritdoc />
    public async Task<AdminBookWorkspaceViewModel?> GetBookWorkspaceAsync(int bookId, CancellationToken cancellationToken = default)
    {
        var book = await _db.Books.AsNoTracking()
            .FirstOrDefaultAsync(b => b.BookId == bookId, cancellationToken);
        if (book == null) return null;

        var owner = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.UserId == book.UserId, cancellationToken);
        var author = await _db.Authors.AsNoTracking()
            .FirstOrDefaultAsync(a => a.AuthorId == book.AuthorId, cancellationToken);

        var (step, path) = await _flow.GetResumeStepAsync(bookId, cancellationToken);
        var maxStepRow = await _db.Settings.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Key == $"book:{bookId}:flowMaxStep", cancellationToken);

        var formatting = await _db.BookFormatting.AsNoTracking()
            .Where(f => f.BookId == bookId)
            .OrderByDescending(f => f.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var price = await _db.BookPrice.AsNoTracking()
            .Where(p => p.BookId == bookId)
            .OrderByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        var chapters = await _db.Chapters.AsNoTracking()
            .Where(c => c.BookId == bookId)
            .OrderBy(c => c.OrderIndex == 0 ? c.ChapterNumber : c.OrderIndex)
            .ThenBy(c => c.ChapterNumber)
            .Select(c => new AdminChapterRow
            {
                ChapterId = c.ChapterId,
                ChapterNumber = c.ChapterNumber > 0 ? c.ChapterNumber : c.OrderIndex,
                Title = c.Title ?? "",
                WordCount = c.WordCount,
                Status = c.Status ?? "",
                IsPublished = c.IsPublished,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var covers = await _db.BookCoverDesigns.AsNoTracking()
            .Where(c => c.BookId == bookId && !c.IsDeleted)
            .OrderByDescending(c => c.IsActive)
            .ThenByDescending(c => c.UpdatedAt)
            .Select(c => new AdminCoverRow
            {
                CoverId = c.CoverId,
                CoverType = c.CoverType,
                DisplayName = c.DisplayName,
                Status = c.Status,
                IsActive = c.IsActive,
                FrontImagePath = c.FrontImagePath,
                UpdatedAt = c.UpdatedAt
            })
            .ToListAsync(cancellationToken);

        var transitions = await _db.BookStateTransitions.AsNoTracking()
            .Where(t => t.BookId == bookId)
            .OrderByDescending(t => t.CreatedAt)
            .Take(40)
            .Select(t => new AdminTransitionRow
            {
                Kind = t.Kind,
                FromStatus = t.FromStatus,
                ToStatus = t.ToStatus,
                CreatedAt = t.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return new AdminBookWorkspaceViewModel
        {
            BookId = book.BookId,
            UserId = book.UserId,
            OwnerName = owner?.FullName ?? author?.FullName ?? "Unknown",
            OwnerEmail = owner?.UserEmail ?? "",
            Title = string.IsNullOrWhiteSpace(book.Title) ? "Untitled" : book.Title,
            Subtitle = book.Subtitle,
            Genre = book.Genre ?? "",
            Status = book.Status ?? "",
            Description = book.Description,
            WordCount = book.WordCount,
            CoverImagePath = book.CoverImagePath,
            CreatedAt = book.CreatedAt,
            UpdatedAt = book.UpdatedAt,
            FlowStep = step,
            FlowLabel = BookFlowStateService.StepToLabel(step),
            FlowPercent = BookFlowStateService.IsPublishedStatus(book.Status) ? 100 : BookFlowStateService.StepToPercent(step),
            FlowPath = path,
            MaxFlowStep = maxStepRow?.Value,
            Format = formatting?.Format,
            InteriorStyle = formatting?.InteriorStyle,
            TextSize = formatting?.TextSize,
            LineSpacing = formatting?.LineSpacing,
            PublishingPlatforms = formatting?.PublishingPlatforms ?? formatting?.PublishingPlatform,
            ListPrice = price?.bookPrice,
            Currency = string.IsNullOrWhiteSpace(price?.Currency) ? "USD" : price!.Currency,
            Chapters = chapters,
            Covers = covers,
            Transitions = transitions
        };
    }

    /// <inheritdoc />
    public async Task EnrichBookRowsAsync(IList<BookManagementViewModel> books, CancellationToken cancellationToken = default)
    {
        if (books.Count == 0) return;
        var ids = books.Select(b => b.BookId).Distinct().ToList();
        var userIds = books.Select(b => b.UserId).Distinct().ToList();

        var chapterCounts = await _db.Chapters.AsNoTracking()
            .Where(c => ids.Contains(c.BookId))
            .GroupBy(c => c.BookId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, cancellationToken);

        var coverIds = (await _db.BookCoverDesigns.AsNoTracking()
            .Where(c => ids.Contains(c.BookId) && !c.IsDeleted)
            .Select(c => c.BookId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        var formatIds = (await _db.BookFormatting.AsNoTracking()
            .Where(f => ids.Contains(f.BookId))
            .Select(f => f.BookId)
            .Distinct()
            .ToListAsync(cancellationToken)).ToHashSet();

        var emails = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.UserId))
            .ToDictionaryAsync(u => u.UserId, u => u.UserEmail, cancellationToken);

        var descriptions = await _db.Books.AsNoTracking()
            .Where(b => ids.Contains(b.BookId))
            .Select(b => new { b.BookId, b.Description })
            .ToDictionaryAsync(b => b.BookId, b => b.Description, cancellationToken);

        var flowKeys = ids.SelectMany(id => new[]
        {
            $"book:{id}:flowStep",
            $"book:{id}:flowMaxStep",
            $"book:{id}:flowPath"
        }).ToList();
        var flowRows = await _db.Settings.AsNoTracking()
            .Where(s => flowKeys.Contains(s.Key))
            .ToDictionaryAsync(s => s.Key, s => s.Value ?? "", cancellationToken);

        foreach (var book in books)
        {
            book.ChapterCount = chapterCounts.GetValueOrDefault(book.BookId);
            book.HasCover = coverIds.Contains(book.BookId) || !string.IsNullOrWhiteSpace(book.CoverImagePath);
            book.HasFormatting = formatIds.Contains(book.BookId);
            book.UserEmail = emails.GetValueOrDefault(book.UserId);
            book.Description = descriptions.GetValueOrDefault(book.BookId);
            var step = flowRows.GetValueOrDefault($"book:{book.BookId}:flowStep", "").Trim();
            var max = flowRows.GetValueOrDefault($"book:{book.BookId}:flowMaxStep", "").Trim();
            if (string.IsNullOrEmpty(step)) step = BookFlowStateService.StepGenerate;
            var resume = BookFlowStateService.StepRank(max) > BookFlowStateService.StepRank(step) ? max : step;
            book.FlowStep = resume;
            book.FlowLabel = BookFlowStateService.StepToLabel(resume);
            book.FlowPercent = BookFlowStateService.IsPublishedStatus(book.Status)
                ? 100
                : BookFlowStateService.StepToPercent(resume);
        }
    }

    /// <inheritdoc />
    public async Task EnrichUserRowsAsync(IList<UserManagementViewModel> users, CancellationToken cancellationToken = default)
    {
        if (users.Count == 0) return;
        var ids = users.Select(u => u.UserId).Distinct().ToList();
        var plans = await _db.AuthorPlans.AsNoTracking()
            .Where(p => ids.Contains(p.UserId) && p.IsActive == 1)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new { p.UserId, p.PlanName })
            .ToListAsync(cancellationToken);
        var map = new Dictionary<int, string>();
        foreach (var p in plans)
        {
            if (!map.ContainsKey(p.UserId))
                map[p.UserId] = p.PlanName;
        }
        foreach (var user in users)
            user.PlanName = map.GetValueOrDefault(user.UserId);
    }

    private static int CalculateProfileCompletion(Users user)
    {
        int completed = 0;
        const int total = 7;
        if (!string.IsNullOrEmpty(user.FullName)) completed++;
        if (!string.IsNullOrEmpty(user.UserEmail)) completed++;
        if (!string.IsNullOrEmpty(user.ProfilePicturePath)) completed++;
        if (!string.IsNullOrEmpty(user.SecretQuestion)) completed++;
        if (!string.IsNullOrEmpty(user.SecretQuestionAnswer)) completed++;
        if (user.CreatedAt != default) completed++;
        if (user.LastLoginAt != default && user.LastLoginAt.Year > 1981) completed++;
        return (int)((double)completed / total * 100);
    }
}
