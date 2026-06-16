using System.Text.Json;
using EBookDashboard.Infrastructure;
using EBookDashboard.Models;
using EBookDashboard.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace EBookDashboard.Services
{
    /// <summary>
    /// Writes cross-cutting admin data after books/subscriptions change: state history, audit, notifications, SignalR.
    /// </summary>
    public sealed class BookLifecycleAfterSaveService
    {
        private readonly IHubContext<AdminActivityHub> _hubContext;
        private readonly ILogger<BookLifecycleAfterSaveService> _logger;

        public BookLifecycleAfterSaveService(
            IHubContext<AdminActivityHub> hubContext,
            ILogger<BookLifecycleAfterSaveService> logger)
        {
            _hubContext = hubContext;
            _logger = logger;
        }

        public async Task ProcessAsync(
            ApplicationDbContext db,
            IReadOnlyList<BookLifecycleQueuedEvent> bookEvents,
            IReadOnlyList<SubscriptionLifecycleQueuedEvent> subscriptionEvents,
            CancellationToken cancellationToken)
        {
            if (bookEvents.Count == 0 && subscriptionEvents.Count == 0)
                return;

            db.SuppressBookLifecycle = true;
            try
            {
                foreach (var e in bookEvents)
                {
                    var b = e.Book;
                    var ownerEmail = await db.Users.AsNoTracking()
                        .Where(u => u.UserId == b.UserId)
                        .Select(u => u.UserEmail)
                        .FirstOrDefaultAsync(cancellationToken);

                    db.BookStateTransitions.Add(new BookStateTransition
                    {
                        BookId = b.BookId,
                        UserId = b.UserId,
                        ActorUserId = e.ActorUserId,
                        FromStatus = e.FromStatus,
                        ToStatus = e.ToStatus,
                        Kind = e.Kind,
                        MetadataJson = JsonSerializer.Serialize(new
                        {
                            b.Title,
                            ownerEmail,
                            b.Genre,
                            b.CreatedAt,
                            b.UpdatedAt
                        }),
                        CreatedAt = DateTime.UtcNow
                    });

                    var auditDesc = e.Kind == "Created"
                        ? $"Book created: \"{b.Title}\" (User {b.UserId})"
                        : $"Book status {b.BookId}: {e.FromStatus ?? "(new)"} → {e.ToStatus}";
                    auditDesc = auditDesc.Length > 500 ? auditDesc[..500] : auditDesc;

                    db.AuditLogs.Add(new AuditLog
                    {
                        UserId = e.ActorUserId,
                        Action = e.Kind == "Created" ? "Create" : "Update",
                        EntityType = "Book",
                        EntityId = b.BookId,
                        Description = auditDesc,
                        IpAddress = null,
                        UserAgent = "BookLifecycle",
                        CreatedAt = DateTime.UtcNow
                    });

                    var notifTitle = e.Kind == "Created" ? "New book created" : "Book status updated";
                    var notifMsg = e.Kind == "Created"
                        ? $"User {b.UserId} ({ownerEmail ?? "?"}) created \"{b.Title}\"."
                        : $"Book #{b.BookId} \"{b.Title}\" moved from {e.FromStatus ?? "?"} to {e.ToStatus}.";

                    db.Notifications.Add(new Notification
                    {
                        UserId = null,
                        Title = notifTitle.Length > 200 ? notifTitle[..200] : notifTitle,
                        Message = notifMsg.Length > 1000 ? notifMsg[..1000] : notifMsg,
                        Type = e.Kind == "Created" ? "Success" : "Info",
                        IsRead = false,
                        Link = "/Admin/ContentManagement",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                foreach (var s in subscriptionEvents)
                {
                    var p = s.Plan;
                    var userEmail = await db.Users.AsNoTracking()
                        .Where(u => u.UserId == p.UserId)
                        .Select(u => u.UserEmail)
                        .FirstOrDefaultAsync(cancellationToken);

                    var isCreate = s.IsNewSubscription;
                    var desc = isCreate
                        ? $"Subscription created #{p.AuthorPlanId} user {p.UserId} plan \"{p.PlanName}\" active={s.NewIsActive}"
                        : $"Subscription #{p.AuthorPlanId} user {p.UserId}: IsActive {s.OldIsActive} → {s.NewIsActive}";
                    desc = desc.Length > 500 ? desc[..500] : desc;

                    db.AuditLogs.Add(new AuditLog
                    {
                        UserId = p.UserId,
                        Action = isCreate ? "Create" : "Update",
                        EntityType = "AuthorPlan",
                        EntityId = p.AuthorPlanId,
                        Description = desc,
                        IpAddress = null,
                        UserAgent = "BookLifecycle",
                        CreatedAt = DateTime.UtcNow
                    });

                    var title = isCreate ? "New subscription" : "Subscription updated";
                    var msg = $"User {p.UserId} ({userEmail ?? "?"}): plan \"{p.PlanName}\" active={s.NewIsActive}.";
                    db.Notifications.Add(new Notification
                    {
                        UserId = null,
                        Title = title.Length > 200 ? title[..200] : title,
                        Message = msg.Length > 1000 ? msg[..1000] : msg,
                        Type = isCreate ? "Success" : "Warning",
                        IsRead = false,
                        Link = "/Admin/Subscriptions",
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await db.SaveChangesAsync(cancellationToken);

                await _hubContext.Clients.Group(AdminActivityHub.AdminGroupName)
                    .SendAsync("AdminStatsChanged", new { books = bookEvents.Count, subscriptions = subscriptionEvents.Count }, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist lifecycle side-effects. Run Scripts/bookstatetransitions.mysql.sql if table is missing.");
                foreach (var entry in db.ChangeTracker.Entries<BookStateTransition>().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
                foreach (var entry in db.ChangeTracker.Entries<AuditLog>().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
                foreach (var entry in db.ChangeTracker.Entries<Notification>().Where(e => e.State == EntityState.Added).ToList())
                    entry.State = EntityState.Detached;
            }
            finally
            {
                db.SuppressBookLifecycle = false;
            }
        }
    }
}
