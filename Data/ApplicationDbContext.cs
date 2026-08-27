using EBookDashboard.Models;
using EBookDashboard.Models.ViewModels;
using EBookDashboard.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Models
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        { }

        // 📚 Book-related
        public DbSet<Books> Books { get; set; }
        public DbSet<BookPrice> BookPrice { get; set; }
        public DbSet<BookVersion> BookVersions { get; set; }
        public DbSet<Chapters> Chapters { get; set; }
        public DbSet<ChapterIteration> ChapterIterations { get; set; }

        // Add this new DbSet for raw responses   apirawresponse
        public DbSet<APIRawResponse> APIRawResponse { get; set; }
        public DbSet<FinalizeChapters> FinalizeChapters { get; set; }
        public DbSet<ParsedBookContent> ParsedBookContent { get; set; }

        public DbSet<BookSelectionsUser> BookSelectionsUser { get; set; }
        public DbSet<BookDesign> BookDesign { get; set; }
        public DbSet<CoverDesignCalculator> coverDesignCalculator { get; set; }
        public DbSet<BookCoverPages> BookCoverPages { get; set; }
        public DbSet<BookFormatting> BookFormatting { get; set; }
        public DbSet<BookCoverDesign> BookCoverDesigns { get; set; }
        // ✍️ Author-related
        public DbSet<Authors> Authors { get; set; }
        public DbSet<AuthorPlans> AuthorPlans { get; set; }
        public DbSet<AuthorPlanFeatures> AuthorPlanFeaturesSet { get; set; } // Renamed to avoid conflict
                                                                             // Make sure this DbSet is defined
      
        public DbSet<AuthorBills> AuthorBills { get; set; }

        // 👥 User-related
        public DbSet<Users> Users { get; set; }
        public DbSet<UserStats> UserStats { get; set; }
        public DbSet<Roles> Roles { get; set; }
        public DbSet<Notification> Notifications { get; set; }
        public DbSet<UserPreference> UserPreferences { get; set; }
        public DbSet<UserFeatures> UserFeatures { get; set; }
        public DbSet<Features> Features { get; set; }
        public DbSet<OtpVerification> OtpVerifications { get; set; }
        public DbSet<PasswordReset> PasswordResets { get; set; }

        // 🏷 Misc / others
        public DbSet<Categories> Categories { get; set; }
        public DbSet<Language> Languages { get; set; }
        public DbSet<Plans> Plans { get; set; }
        public DbSet<PlanFeatures> PlanFeatures { get; set; }
        public DbSet<PubCost> PubCosts { get; set; }
        public DbSet<RecordStatus> RecordStatus { get; set; }
        public DbSet<Settings> Settings { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<PricingRule> PricingRules { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderItem> OrderItems { get; set; }
        public DbSet<Entitlement> Entitlements { get; set; }
        public DbSet<BookBillingState> BookBillingStates { get; set; }
        public DbSet<FormattingTemplate> FormattingTemplates { get; set; }
        public DbSet<BookStateTransition> BookStateTransitions { get; set; }

        // KDP publishing pipeline (extends existing Books model; does not replace it)
        public DbSet<Project> Projects { get; set; }
        public DbSet<ManuscriptVersion> ManuscriptVersions { get; set; }
        public DbSet<BookSection> BookSections { get; set; }
        public DbSet<LayoutProfile> LayoutProfiles { get; set; }
        public DbSet<CoverProject> CoverProjects { get; set; }
        public DbSet<ExportJob> ExportJobs { get; set; }

        public object? AuthorPlanFeatures { get; internal set; }

        /// <summary>When true, lifecycle interceptor does not queue book/subscription side-effects (avoids re-entrancy).</summary>
        internal bool SuppressBookLifecycle { get; set; }

        internal List<BookLifecycleQueuedEvent> LifecycleQueue { get; } = new();

        internal List<SubscriptionLifecycleQueuedEvent> SubscriptionLifecycleQueue { get; } = new();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            // Ignore CategoriesCategoryId in Books entity
            // modelBuilder.Entity<Books>().Ignore(b => b.CategoriesCategoryId);
            // Configure PasswordReset entity
            modelBuilder.Entity<PasswordReset>(entity =>
            {
                entity.HasKey(e => e.ResetId);
                entity.Property(e => e.Email).IsRequired().HasMaxLength(255);
                entity.Property(e => e.OTP).IsRequired().HasMaxLength(6);
                entity.Property(e => e.Token).HasMaxLength(255);
                entity.HasIndex(e => new { e.Email, e.OTP, e.IsUsed });
            });

            modelBuilder.Entity<Plans>().HasData(
                new Plans { PlanId = 1, PlanName = "Free Trial", PlanRate = 0, PlanDays = 30, PlanDescription = "Free 1-month trial", CreateddAt = new DateTime(2026, 7, 27, 18, 8, 4, 658, DateTimeKind.Utc).AddTicks(9162) },
                new Plans { PlanId = 2, PlanName = "Basic Plan", PlanRate = 9.99m, PlanDays = 30, PlanDescription = "Basic monthly subscription", CreateddAt = new DateTime(2026, 7, 27, 18, 8, 4, 658, DateTimeKind.Utc).AddTicks(9171) },
                new Plans { PlanId = 3, PlanName = "Pro Plan", PlanRate = 99.99m, PlanDays = 365, PlanDescription = "Yearly subscription", CreateddAt = new DateTime(2026, 7, 27, 18, 8, 4, 658, DateTimeKind.Utc).AddTicks(9176) });

            // Seed Roles data
            modelBuilder.Entity<Roles>().HasData(
                new Roles { RoleId = 1, RoleName = "Admin", Description = "Administrator with full access", AllowDownloads = true, AllowFullDashboard = true, AllowAnalytics = true, AllowPublishing = true, AllowDelete = true, AllowEdit = true },
                new Roles { RoleId = 2, RoleName = "Author", Description = "Author with publishing access", AllowDownloads = true, AllowFullDashboard = true, AllowAnalytics = true, AllowPublishing = true, AllowDelete = true, AllowEdit = true },
                new Roles { RoleId = 3, RoleName = "Reader", Description = "Reader with limited access", AllowDownloads = false, AllowFullDashboard = false, AllowAnalytics = false, AllowPublishing = false, AllowDelete = false, AllowEdit = false });

            var pricingSeedUpdatedAt = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc);
            modelBuilder.Entity<PricingRule>().HasData(
                new PricingRule { Id = 1, Key = "writing.per_page", DisplayName = "AI writing", Description = null, Unit = PricingUnit.PerPage, UnitAmount = 0.50m, FreeAllowance = 20, Currency = "USD", IsActive = true, UpdatedAtUtc = pricingSeedUpdatedAt },
                new PricingRule { Id = 2, Key = "cover.custom_image", DisplayName = "Custom image cover", Description = null, Unit = PricingUnit.PerItem, UnitAmount = 10.00m, FreeAllowance = 0, Currency = "USD", IsActive = true, UpdatedAtUtc = pricingSeedUpdatedAt },
                new PricingRule { Id = 3, Key = "formatting.premium", DisplayName = "Premium formatting", Description = null, Unit = PricingUnit.PerItem, UnitAmount = 3.00m, FreeAllowance = 0, Currency = "USD", IsActive = true, UpdatedAtUtc = pricingSeedUpdatedAt },
                new PricingRule { Id = 4, Key = "export.paperback", DisplayName = "Paperback package", Description = null, Unit = PricingUnit.Flat, UnitAmount = 20.00m, FreeAllowance = 0, Currency = "USD", IsActive = true, UpdatedAtUtc = pricingSeedUpdatedAt },
                new PricingRule { Id = 5, Key = "export.hardcover", DisplayName = "Hardcover package", Description = null, Unit = PricingUnit.Flat, UnitAmount = 30.00m, FreeAllowance = 0, Currency = "USD", IsActive = true, UpdatedAtUtc = pricingSeedUpdatedAt });

            // ✅ Decimal precision for MySQL
            modelBuilder.Entity<BookPrice>()
                .Property(b => b.bookPrice)
                .HasPrecision(10, 2);

            modelBuilder.Entity<Plans>()
                .Property(p => p.PlanRate)
                .HasPrecision(10, 2);

            modelBuilder.Entity<PubCost>()
                .Property(p => p.Amount)
                .HasPrecision(10, 2);

            modelBuilder.Entity<PlanFeatures>()
                .Property(p => p.FeatureRate)
                .HasPrecision(10, 2);

            modelBuilder.Entity<AuthorPlanFeatures>()
                .Property(p => p.FeatureRate)
                .HasPrecision(10, 2);

            modelBuilder.Entity<AuthorPlanFeatures>()
                .Property(p => p.TotalAmount)
                .HasPrecision(10, 2);

            modelBuilder.Entity<Books>()
                .Property(b => b.BookContentHtml)
                .HasColumnType("longtext");

            modelBuilder.Entity<BookCoverDesign>(e =>
            {
                e.HasIndex(x => new { x.BookId, x.UserId, x.IsDeleted });
                e.HasIndex(x => new { x.BookId, x.IsActive });
                e.Property(x => x.TrimWidthIn).HasPrecision(10, 4);
                e.Property(x => x.TrimHeightIn).HasPrecision(10, 4);
                e.Property(x => x.SpineWidthIn).HasPrecision(10, 4);
                e.Property(x => x.BleedIn).HasPrecision(10, 4);
                e.Property(x => x.FullWidthIn).HasPrecision(10, 4);
                e.Property(x => x.FullHeightIn).HasPrecision(10, 4);
            });

            // ✅ Example: Unique constraint on AuthorCode
            modelBuilder.Entity<Authors>()
                .HasIndex(a => a.AuthorCode)
                .IsUnique();

            // ✅ Example: Relationships (optional, add as needed)
            modelBuilder.Entity<Books>()
                .HasOne<Authors>()
                .WithMany(a => a.Books)
                .HasForeignKey(b => b.AuthorId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Chapters>()
                .HasOne<Books>()
                .WithMany(b => b.Chapters)
                .HasForeignKey(c => c.BookId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ChapterIteration>(e =>
            {
                e.HasIndex(x => new { x.UserId, x.BookId, x.ChapterNumber });
                e.HasIndex(x => x.ChapterSeriesGuid);
                e.HasIndex(x => new { x.UserId, x.BookId, x.IsFinalized, x.ChapterNumber });
                e.HasIndex(x => x.ResponseId).IsUnique();
            });

            // Ensure Users table has a foreign key relationship to Roles
            modelBuilder.Entity<Users>()
                .HasOne(u => u.Role)
                .WithMany(r => r.Users)
                .HasForeignKey(u => u.RoleId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure relationships for feature tables
            modelBuilder.Entity<AuthorPlanFeatures>()
                .HasOne(apf => apf.PlanFeature)
                .WithMany()
                .HasForeignKey(apf => apf.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);
            // Configure UserFeatures to Features relationship
            modelBuilder.Entity<UserFeatures>()
                .HasOne(uf => uf.Feature)
                .WithMany(f => f.UserFeatures)
                .HasForeignKey(uf => uf.FeatureId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ensure relationship between AuthorPlanFeatures and AuthorBills via BillId
            modelBuilder.Entity<AuthorPlanFeatures>()
                .HasOne(apf => apf.AuthorBill)
                .WithMany(b => b.AuthorPlanFeatures)
                .HasForeignKey(apf => apf.BillId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure Notification relationships
            modelBuilder.Entity<Notification>()
                .HasOne(n => n.User)
                .WithMany()
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Configure UserPreference relationships
            modelBuilder.Entity<UserPreference>()
                .HasOne(up => up.User)
                .WithMany()
                .HasForeignKey(up => up.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure AuditLog relationships
            modelBuilder.Entity<AuditLog>()
                .HasOne(al => al.User)
                .WithMany()
                .HasForeignKey(al => al.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<BookStateTransition>(e =>
            {
                e.HasKey(x => x.BookStateTransitionId);
                e.Property(x => x.ToStatus).HasMaxLength(100).IsRequired();
                e.Property(x => x.FromStatus).HasMaxLength(100);
                e.Property(x => x.Kind).HasMaxLength(50).IsRequired();
                e.Property(x => x.MetadataJson).HasMaxLength(2000);
                e.HasIndex(x => x.BookId);
                e.HasIndex(x => x.UserId);
                e.HasIndex(x => x.CreatedAt);
            });

            // Unique constraint on Settings Key
            modelBuilder.Entity<Settings>()
                .HasIndex(s => s.Key)
                .IsUnique();

            modelBuilder.Entity<PricingRule>(e =>
            {
                e.HasIndex(x => x.Key).IsUnique();
                e.Property(x => x.Key).IsRequired().HasMaxLength(64);
                e.Property(x => x.DisplayName).IsRequired().HasMaxLength(128);
                e.Property(x => x.Description).HasMaxLength(512);
                e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
                e.Property(x => x.UnitAmount).HasPrecision(18, 2);
            });

            modelBuilder.Entity<Order>(e =>
            {
                e.HasIndex(x => x.StripeCheckoutSessionId).IsUnique();
                e.HasIndex(x => x.UserId);
                e.HasIndex(x => x.BookId);
                e.Property(x => x.Subtotal).HasPrecision(18, 2);
                e.Property(x => x.Discount).HasPrecision(18, 2);
                e.Property(x => x.Total).HasPrecision(18, 2);
                e.Property(x => x.Currency).IsRequired().HasMaxLength(3);
                e.HasMany(x => x.Items).WithOne(x => x.Order!).HasForeignKey(x => x.OrderId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<OrderItem>(e =>
            {
                e.Property(x => x.UnitAmount).HasPrecision(18, 2);
                e.Property(x => x.LineTotal).HasPrecision(18, 2);
                e.Property(x => x.PricingRuleKey).IsRequired().HasMaxLength(64);
            });

            modelBuilder.Entity<Entitlement>(e =>
            {
                e.HasIndex(x => new { x.UserId, x.BookId, x.Type, x.ScopeKey });
                e.HasIndex(x => x.OrderId);
            });

            modelBuilder.Entity<BookBillingState>(e =>
            {
                e.HasIndex(x => x.BookId).IsUnique();
            });

            modelBuilder.Entity<FormattingTemplate>(e =>
            {
                e.HasIndex(x => x.StyleKey).IsUnique();
                e.Property(x => x.PremiumPrice).HasPrecision(18, 2);
                e.HasData(
                    new FormattingTemplate { Id = 1, StyleKey = "Novel", DisplayName = "Novel", IsPremium = false },
                    new FormattingTemplate { Id = 2, StyleKey = "Traditional", DisplayName = "Traditional", IsPremium = false },
                    new FormattingTemplate { Id = 3, StyleKey = "Contemporary", DisplayName = "Contemporary", IsPremium = false },
                    new FormattingTemplate { Id = 4, StyleKey = "Modern", DisplayName = "Modern", IsPremium = false },
                    new FormattingTemplate { Id = 5, StyleKey = "Classic", DisplayName = "Classic", IsPremium = false },
                    new FormattingTemplate { Id = 6, StyleKey = "Clean", DisplayName = "Clean", IsPremium = false },
                    new FormattingTemplate { Id = 7, StyleKey = "Minimalist", DisplayName = "Minimalist", IsPremium = false },
                    new FormattingTemplate { Id = 8, StyleKey = "POD", DisplayName = "POD", IsPremium = false },
                    new FormattingTemplate { Id = 9, StyleKey = "FineBook", DisplayName = "Fine Book", IsPremium = true, PremiumPrice = null },
                    new FormattingTemplate { Id = 10, StyleKey = "ElegantTrade", DisplayName = "Elegant Trade", IsPremium = true, PremiumPrice = null },
                    new FormattingTemplate { Id = 11, StyleKey = "ElegantTradePOD", DisplayName = "Elegant Trade (POD)", IsPremium = true, PremiumPrice = null });
            });

            modelBuilder.Entity<Settings>()
                .Property(s => s.Value)
                .HasColumnType("longtext");

            ConfigurePublishingModel(modelBuilder);
        }

        private static void ConfigurePublishingModel(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Project>(e =>
            {
                e.HasIndex(x => x.UserId);
                e.Property(x => x.TrimWidthIn).HasPrecision(10, 4);
                e.Property(x => x.TrimHeightIn).HasPrecision(10, 4);
                e.HasMany(x => x.ManuscriptVersions).WithOne(x => x.Project!).HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.LayoutProfiles).WithOne(x => x.Project!).HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.CoverProjects).WithOne(x => x.Project!).HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.ExportJobs).WithOne(x => x.Project!).HasForeignKey(x => x.ProjectId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<ManuscriptVersion>(e =>
            {
                e.HasIndex(x => new { x.ProjectId, x.VersionNumber }).IsUnique();
                e.HasIndex(x => new { x.ProjectId, x.IsActive });
                e.HasMany(x => x.Sections).WithOne(x => x.ManuscriptVersion!).HasForeignKey(x => x.ManuscriptVersionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<BookSection>(e =>
            {
                e.HasIndex(x => new { x.ManuscriptVersionId, x.OrderIndex });
                e.HasOne(x => x.ParentSection).WithMany(x => x.ChildSections)
                    .HasForeignKey(x => x.ParentSectionId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<LayoutProfile>(e =>
            {
                e.HasIndex(x => new { x.ProjectId, x.IsEbookProfile }).IsUnique();
                e.Property(x => x.MarginTopIn).HasPrecision(10, 4);
                e.Property(x => x.MarginBottomIn).HasPrecision(10, 4);
                e.Property(x => x.MarginOutsideIn).HasPrecision(10, 4);
                e.Property(x => x.MarginInsideIn).HasPrecision(10, 4);
                e.Property(x => x.FirstLineIndentIn).HasPrecision(10, 4);
            });

            modelBuilder.Entity<CoverProject>(e =>
            {
                e.HasIndex(x => x.ProjectId);
                e.Property(x => x.ComputedSpineWidthIn).HasPrecision(10, 4);
                e.Property(x => x.ComputedTotalWidthIn).HasPrecision(10, 4);
                e.Property(x => x.ComputedTotalHeightIn).HasPrecision(10, 4);
            });

            modelBuilder.Entity<ExportJob>(e =>
            {
                e.HasIndex(x => new { x.ProjectId, x.Status });
            });
        }

        /// <summary>
        /// Next <see cref="Settings.SettingId"/> for INSERTs when MySQL does not define <c>AUTO_INCREMENT</c> on that column
        /// (avoids: Field 'SettingId' doesn't have a default value).
        /// </summary>
        public async Task<int> NextSettingIdAsync(CancellationToken cancellationToken = default)
        {
            var max = await Settings.AsNoTracking()
                .Select(s => (int?)s.SettingId)
                .MaxAsync(cancellationToken);
            return (max ?? 0) + 1;
        }
    }
}