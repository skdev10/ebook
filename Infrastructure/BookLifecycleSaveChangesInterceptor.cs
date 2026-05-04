using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EBookDashboard.Infrastructure
{
    /// <summary>
    /// Queues book and subscription lifecycle events during SaveChanges; after save, <see cref="Services.BookLifecycleAfterSaveService"/> persists notifications, audit rows, and state history.
    /// </summary>
    public sealed class BookLifecycleSaveChangesInterceptor : ISaveChangesInterceptor
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<BookLifecycleSaveChangesInterceptor> _logger;

        public BookLifecycleSaveChangesInterceptor(
            IServiceScopeFactory scopeFactory,
            ILogger<BookLifecycleSaveChangesInterceptor> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        public InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            CaptureQueues(eventData.Context);
            return result;
        }

        public ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            CaptureQueues(eventData.Context);
            return ValueTask.FromResult(result);
        }

        public int SavedChanges(SaveChangesCompletedEventData eventData, int result)
            => SavedChangesAsync(eventData, result, default).AsTask().GetAwaiter().GetResult();

        public async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            await ProcessAfterSaveAsync(eventData, result, cancellationToken);
            return result;
        }

        private static void CaptureQueues(DbContext? context)
        {
            if (context is not ApplicationDbContext app || app.SuppressBookLifecycle)
                return;

            app.LifecycleQueue.Clear();
            app.SubscriptionLifecycleQueue.Clear();

            foreach (var entry in app.ChangeTracker.Entries<Books>())
            {
                if (entry.State == EntityState.Added)
                {
                    var to = string.IsNullOrWhiteSpace(entry.Entity.Status) ? "Draft" : entry.Entity.Status;
                    app.LifecycleQueue.Add(new BookLifecycleQueuedEvent("Created", entry.Entity, null, to, entry.Entity.UserId));
                }
                else if (entry.State == EntityState.Modified)
                {
                    var statusProp = entry.Property(nameof(Books.Status));
                    if (!statusProp.IsModified)
                        continue;
                    var from = statusProp.OriginalValue?.ToString();
                    var to = statusProp.CurrentValue?.ToString() ?? "";
                    if (string.Equals(from, to, StringComparison.Ordinal))
                        continue;
                    app.LifecycleQueue.Add(new BookLifecycleQueuedEvent("StatusChanged", entry.Entity, from, to, entry.Entity.UserId));
                }
            }

            foreach (var entry in app.ChangeTracker.Entries<AuthorPlans>())
            {
                if (entry.State == EntityState.Added)
                {
                    app.SubscriptionLifecycleQueue.Add(new SubscriptionLifecycleQueuedEvent(
                        entry.Entity, null, entry.Entity.IsActive, null, entry.Entity.EndDate, isNewSubscription: true));
                    continue;
                }

                if (entry.State != EntityState.Modified)
                    continue;
                var activeProp = entry.Property(nameof(AuthorPlans.IsActive));
                var endProp = entry.Property(nameof(AuthorPlans.EndDate));
                if (!activeProp.IsModified && !endProp.IsModified)
                    continue;
                var oldActive = activeProp.IsModified ? (int?)activeProp.OriginalValue : null;
                var newActive = entry.Entity.IsActive;
                var oldEnd = endProp.IsModified ? (DateTime?)endProp.OriginalValue : null;
                var newEnd = entry.Entity.EndDate;
                app.SubscriptionLifecycleQueue.Add(new SubscriptionLifecycleQueuedEvent(
                    entry.Entity, oldActive, newActive, oldEnd, newEnd, isNewSubscription: false));
            }
        }

        private async Task ProcessAfterSaveAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken)
        {
            if (result <= 0 || eventData.Context is not ApplicationDbContext app || app.SuppressBookLifecycle)
                return;

            var bookEvents = app.LifecycleQueue.ToList();
            var subEvents = app.SubscriptionLifecycleQueue.ToList();
            app.LifecycleQueue.Clear();
            app.SubscriptionLifecycleQueue.Clear();

            if (bookEvents.Count == 0 && subEvents.Count == 0)
                return;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<BookLifecycleAfterSaveService>();
                await processor.ProcessAsync(app, bookEvents, subEvents, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BookLifecycle after-save processing failed (ensure bookstatetransitions table exists).");
            }
        }
    }
}
