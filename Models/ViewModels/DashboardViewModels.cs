using System.ComponentModel.DataAnnotations;

namespace EBookDashboard.Models
{
    public class DashboardIndexViewModel
    {
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public int TotalBooksPublished { get; set; }
        public decimal MonthlyRevenue { get; set; }
        public int TotalDownloads { get; set; }
        public decimal AverageRating { get; set; }
        public List<ProjectViewModel> CurrentProjects { get; set; } = new List<ProjectViewModel>();
        public List<ActivityViewModel> RecentActivities { get; set; } = new List<ActivityViewModel>();
        public BookViewModel CurrentWorkingBook { get; set; } = new BookViewModel();
        public int TotalBooksGenerated { get; set; }
        public int BooksRead { get; set; }
        public int HoursRead { get; set; }
        public int PagesRead { get; set; }
        public int DayStreak { get; set; }
        /// <summary>Demo data: hero book (e.g. The Wizarding Chronicles).</summary>
        public string? DemoHeroTitle { get; set; }
        public string? DemoHeroDescription { get; set; }
        public string? DemoHeroCoverUrl { get; set; }
        public int? DemoHeroBookId { get; set; }
        public string? DemoHeroResumeUrl { get; set; }
        /// <summary>Demo data: current read in sidebar (e.g. The Cambers of Secrets, 154/300).</summary>
        public string? DemoCurrentReadTitle { get; set; }
        public string? DemoCurrentReadProgressLabel { get; set; }
        public int DemoCurrentReadPercent { get; set; }
        public string? DemoCurrentReadCoverUrl { get; set; }
        public int? DemoCurrentReadBookId { get; set; }
        /// <summary>Demo published books (same as image).</summary>
        public List<DemoPublishedBookViewModel> DemoPublishedBooks { get; set; } = new List<DemoPublishedBookViewModel>();
        /// <summary>Demo drafts (same as image).</summary>
        public List<DemoDraftViewModel> DemoDrafts { get; set; } = new List<DemoDraftViewModel>();
        /// <summary>Demo reader friends in sidebar.</summary>
        public List<DemoReaderFriendViewModel> DemoReaderFriends { get; set; } = new List<DemoReaderFriendViewModel>();
        /// <summary>True when the signed-in user has at least one book row (any status).</summary>
        public bool HasAnyBooks { get; set; }
        /// <summary>URL to create a draft book and open AI Writer (empty-state primary CTA).</summary>
        public string StartNewBookUrl { get; set; } = "/Dashboard/StartNewBook";
    }

    public class DashboardProfileViewModel
    {
        public string UserName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string UserRole { get; set; } = string.Empty;
        public DateTime MemberSince { get; set; }
        public string Country { get; set; } = string.Empty;
        public int TotalBooks { get; set; }
        public int BooksReading { get; set; }
        public int Reviews { get; set; }
        public int BooksRead { get; set; }
        public int ReadingHours { get; set; }
        public int PagesRead { get; set; }
        public int ReadingStreak { get; set; }
        public string PlanName { get; set; } = string.Empty;
        public decimal PlanPrice { get; set; }
        public DateTime NextBillingDate { get; set; }
        public string BillingCycle { get; set; } = string.Empty;
        public string PaymentMethod { get; set; } = string.Empty;
        public List<PlanFeatureViewModel> PlanFeatures { get; set; } = new List<PlanFeatureViewModel>();
    }

    public class ProjectViewModel
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int ProgressPercentage { get; set; }
        public string ProgressText { get; set; } = string.Empty;
        public string FlowStepLabel { get; set; } = string.Empty;
        public string ResumeUrl { get; set; } = string.Empty;
        public string? CoverImagePath { get; set; }
        public DateTime? LastEditedAt { get; set; }
        public string LastEditedText { get; set; } = string.Empty;
    }

    public class ActivityViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string TimeAgo { get; set; } = string.Empty;
        public string IconClass { get; set; } = string.Empty;
    }

    public class BookViewModel
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string BookIdText { get; set; } = string.Empty;
        public int ProgressPercentage { get; set; }
        public string ProgressText { get; set; } = string.Empty;
        public string? CoverImagePath { get; set; }
    }

    public class PlanFeatureViewModel
    {
        public string Name { get; set; } = string.Empty;
        public bool IsIncluded { get; set; }
    }

    public class DashboardProjectViewModel
    {
        public int BookId { get; set; }
        public string? Title { get; set; }
        public string? Status { get; set; }
        public int ProgressPercentage { get; set; }
        public string? ProgressText { get; set; }
    }

    // New ViewModel for Payment Summary
    public class PaymentSummaryViewModel
    {
        public List<CartItemViewModel> CartItems { get; set; } = new List<CartItemViewModel>();
        public decimal Subtotal { get; set; }
        public decimal Tax { get; set; }
        public decimal Discount { get; set; }
        public decimal Total { get; set; }
    }

    public class CartItemViewModel
    {
        public int FeatureId { get; set; }
        public string FeatureName { get; set; } = "";
        public string Description { get; set; } = "";
        public decimal FeatureRate { get; set; }
    }

    // Admin Dashboard ViewModels
    public class AdminDashboardViewModel
    {
        public int TotalUsers { get; set; }
        public int TotalBooks { get; set; }
        public decimal TotalRevenue { get; set; }
        public int ActiveUsers { get; set; }
        public List<UserManagementViewModel> RecentUsers { get; set; } = new List<UserManagementViewModel>();
        public List<BookManagementViewModel> RecentBooks { get; set; } = new List<BookManagementViewModel>();
        public AnalyticsViewModel Analytics { get; set; } = new AnalyticsViewModel();
    }

    public class UserManagementViewModel
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string UserEmail { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string SignupMethod { get; set; } = "Manual"; // Manual, Google, Facebook
        public int ProfileCompletionPercentage { get; set; }
        public int BooksCreated { get; set; }
    }

    public class BookManagementViewModel
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // Draft, Styled, Previewed, Generated, Published
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? CoverImagePath { get; set; }
        public int WordCount { get; set; }
        public string Genre { get; set; } = string.Empty;
        public int UserId { get; set; }
        public int AuthorId { get; set; }
    }

    /// <summary>Admin Product Management — book catalog with optional list price from BookPrice.</summary>
    public class AdminProductRowViewModel
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Genre { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int WordCount { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public decimal? ListPrice { get; set; }
        public string Currency { get; set; } = "USD";
    }

    public class ProgressTrackerViewModel
    {
        public Dictionary<string, int> UsersByPhase { get; set; } = new Dictionary<string, int>();
        public Dictionary<string, int> BooksByPhase { get; set; } = new Dictionary<string, int>();
        public List<UserJourneyViewModel> UserJourneys { get; set; } = new List<UserJourneyViewModel>();
        public List<BookProgressViewModel> BookProgresses { get; set; } = new List<BookProgressViewModel>();
    }

    public class UserJourneyViewModel
    {
        public int UserId { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string CurrentPhase { get; set; } = string.Empty;
        public DateTime SignupDate { get; set; }
        public DateTime? DraftDate { get; set; }
        public DateTime? GeneratedDate { get; set; }
        public DateTime? PublishedDate { get; set; }
        public int DaysInCurrentPhase { get; set; }
    }

    public class BookProgressViewModel
    {
        public int BookId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string AuthorName { get; set; } = string.Empty;
        public string CurrentPhase { get; set; } = string.Empty;
        public int ProgressPercentage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUpdated { get; set; }
    }

    public class AnalyticsViewModel
    {
        public int TotalUsers { get; set; }
        public int TotalBooks { get; set; }
        public decimal TotalRevenue { get; set; }
        public int TotalActivity { get; set; }
        public List<TimeSeriesData> SignupsOverTime { get; set; } = new List<TimeSeriesData>();
        public List<TimeSeriesData> BookCreationsOverTime { get; set; } = new List<TimeSeriesData>();
        public List<TimeSeriesData> PurchasesOverTime { get; set; } = new List<TimeSeriesData>();
        public ConversionFunnelViewModel ConversionFunnel { get; set; } = new ConversionFunnelViewModel();
    }

    public class TimeSeriesData
    {
        public string Date { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    public class ConversionFunnelViewModel
    {
        public int Signups { get; set; }
        public int Drafts { get; set; }
        public int Styled { get; set; }
        public int Previewed { get; set; }
        public int Generated { get; set; }
        public int Published { get; set; }
        public decimal SignupToDraftRate { get; set; }
        public decimal DraftToPublishedRate { get; set; }
    }

    /// <summary>Book row for Publish page dropdown.</summary>
    public class PublishBookPickerItem
    {
        public int BookId { get; set; }
        public string Title { get; set; } = "Untitled";
        public int ExportableChapterCount { get; set; }
        public bool CanExport { get; set; }
    }

    /// <summary>Demo display item for Published Books section (title, author, cover URL).</summary>
    public class DemoPublishedBookViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public string? Subtitle { get; set; }
        public string CoverUrl { get; set; } = string.Empty;
        /// <summary>Relative web path to saved EPUB when export completed (e.g. /uploads/…/book.epub).</summary>
        public string? EpubUrl { get; set; }
        public int BookId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string LastEditedText { get; set; } = string.Empty;
    }

    /// <summary>Demo display item for Drafts section.</summary>
    public class DemoDraftViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Subtitle { get; set; } = string.Empty;
        public string Volumes { get; set; } = string.Empty;
        public string CoverUrl { get; set; } = string.Empty;
        public int BookId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string LastEditedText { get; set; } = string.Empty;
        public int FlowStepPercent { get; set; }
        public string FlowStepLabel { get; set; } = string.Empty;
        public string ResumeUrl { get; set; } = string.Empty;
    }

    /// <summary>Demo display for Reader Friends in sidebar.</summary>
    public class DemoReaderFriendViewModel
    {
        public string Name { get; set; } = string.Empty;
        public string Initial { get; set; } = string.Empty;
        public string AvatarColor { get; set; } = "bg-gray-400";
        public string TimeAgo { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public string? Tag { get; set; }
    }

    /// <summary>Request body for hard-resetting a book flow step on back navigation.</summary>
    public class ResetFlowStepRequest
    {
        public int BookId { get; set; }
        public string? Step { get; set; }
        /// <summary>When true (leaving to AI Writer), wipe cover + formatting and regress to generate step.</summary>
        public bool WipeAllWork { get; set; }

        /// <summary>When true (confirmed back within flow), wipe the step being left and move one step backward.</summary>
        public bool DestructiveBack { get; set; }
    }

    /// <summary>Request body for <c>POST /Dashboard/ResetEditorDraft</c>.</summary>
    public class EditorDraftResetRequest
    {
        public int BookId { get; set; }
        /// <summary>StepBack | BackToWriter | FullProject | FullProjectWithChapters</summary>
        public string? Scope { get; set; }
        public string? CurrentStep { get; set; }
    }
}