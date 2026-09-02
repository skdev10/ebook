namespace EBookDashboard.Models.ViewModels;

/// <summary>Admin 360 view of a user as they appear on the user dashboard.</summary>
public sealed class AdminUserWorkspaceViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = "";
    public string UserEmail { get; set; } = "";
    public string RoleName { get; set; } = "";
    public string Status { get; set; } = "";
    public string SignupMethod { get; set; } = "";
    public string? ProfilePicturePath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastLoginAt { get; set; }
    public int ProfileCompletionPercentage { get; set; }
    public bool HasCompletedTour { get; set; }

    public string? PlanName { get; set; }
    public decimal? PlanRate { get; set; }
    public DateTime? PlanStart { get; set; }
    public DateTime? PlanEnd { get; set; }
    public bool PlanActive { get; set; }

    public int BookCount { get; set; }
    public int PublishedCount { get; set; }
    public int DraftCount { get; set; }
    public int ChapterCount { get; set; }
    public int CoverCount { get; set; }
    public decimal LifetimeBilled { get; set; }

    public List<AdminBookWorkspaceSummary> Books { get; set; } = new();
    public List<AdminPaymentRow> Payments { get; set; } = new();
    public List<AdminNotificationRow> Notifications { get; set; } = new();
}

/// <summary>Compact book row matching user-panel flow (Writer → Format → Cover → Publish).</summary>
public sealed class AdminBookWorkspaceSummary
{
    public int BookId { get; set; }
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string Genre { get; set; } = "";
    public int WordCount { get; set; }
    public int ChapterCount { get; set; }
    public string FlowStep { get; set; } = "generate";
    public string FlowLabel { get; set; } = "AI Writer";
    public int FlowPercent { get; set; }
    public string FlowPath { get; set; } = "ebook";
    public bool HasCover { get; set; }
    public bool HasFormatting { get; set; }
    public string? CoverImagePath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

/// <summary>Admin 360 view of a single book across every user-panel screen.</summary>
public sealed class AdminBookWorkspaceViewModel
{
    public int BookId { get; set; }
    public int UserId { get; set; }
    public string OwnerName { get; set; } = "";
    public string OwnerEmail { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Subtitle { get; set; }
    public string Genre { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Description { get; set; }
    public int WordCount { get; set; }
    public string? CoverImagePath { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public string FlowStep { get; set; } = "generate";
    public string FlowLabel { get; set; } = "AI Writer";
    public int FlowPercent { get; set; }
    public string FlowPath { get; set; } = "ebook";
    public string? MaxFlowStep { get; set; }

    public string? Format { get; set; }
    public string? InteriorStyle { get; set; }
    public string? TextSize { get; set; }
    public string? LineSpacing { get; set; }
    public string? PublishingPlatforms { get; set; }

    public decimal? ListPrice { get; set; }
    public string Currency { get; set; } = "USD";

    public List<AdminChapterRow> Chapters { get; set; } = new();
    public List<AdminCoverRow> Covers { get; set; } = new();
    public List<AdminTransitionRow> Transitions { get; set; } = new();
}

public sealed class AdminChapterRow
{
    public int ChapterId { get; set; }
    public int ChapterNumber { get; set; }
    public string Title { get; set; } = "";
    public int WordCount { get; set; }
    public string Status { get; set; } = "";
    public bool IsPublished { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AdminCoverRow
{
    public int CoverId { get; set; }
    public string CoverType { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Status { get; set; } = "";
    public bool IsActive { get; set; }
    public string? FrontImagePath { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AdminTransitionRow
{
    public string Kind { get; set; } = "";
    public string? FromStatus { get; set; }
    public string ToStatus { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

public sealed class AdminPaymentRow
{
    public int BillId { get; set; }
    public string Description { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "usd";
    public string? Status { get; set; }
    public string? PaymentReference { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdminNotificationRow
{
    public int NotificationId { get; set; }
    public string Title { get; set; } = "";
    public string Message { get; set; } = "";
    public string Type { get; set; } = "";
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public sealed class AdminCoverManagementViewModel
{
    public int TotalCovers { get; set; }
    public int CoversToday { get; set; }
    public int ActiveCovers { get; set; }
    public int FailedCovers { get; set; }
    public string CoverPrice { get; set; } = "2.99";
    public string DailyLimit { get; set; } = "100";
    public string MonthlyLimit { get; set; } = "2000";
    public string DefaultPrompt { get; set; } = "";
    public List<AdminCoverManagementRow> Recent { get; set; } = new();
}

public sealed class AdminCoverManagementRow
{
    public int CoverId { get; set; }
    public int BookId { get; set; }
    public string BookTitle { get; set; } = "";
    public string UserName { get; set; } = "";
    public string CoverType { get; set; } = "";
    public string Status { get; set; } = "";
    public string? FrontImagePath { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class AdminCmsContentViewModel
{
    public string HeroTitle { get; set; } = "";
    public string HeroDescription { get; set; } = "";
    public string Features { get; set; } = "";
    public string Terms { get; set; } = "";
    public string Privacy { get; set; } = "";
    public string Faq { get; set; } = "";
    public string VideoLinks { get; set; } = "";
    public string ArticleLinks { get; set; } = "";
}
