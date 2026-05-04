namespace EBookDashboard.Infrastructure
{
    public sealed class BookLifecycleQueuedEvent
    {
        public BookLifecycleQueuedEvent(string kind, Models.Books book, string? fromStatus, string? toStatus, int? actorUserId = null)
        {
            Kind = kind;
            Book = book;
            FromStatus = fromStatus;
            ToStatus = toStatus ?? string.Empty;
            ActorUserId = actorUserId;
        }

        public string Kind { get; }
        public Models.Books Book { get; }
        public string? FromStatus { get; }
        public string ToStatus { get; }
        public int? ActorUserId { get; }
    }

    public sealed class SubscriptionLifecycleQueuedEvent
    {
        public SubscriptionLifecycleQueuedEvent(Models.AuthorPlans plan, int? oldActive, int newActive, DateTime? oldEnd, DateTime newEnd, bool isNewSubscription)
        {
            Plan = plan;
            OldIsActive = oldActive;
            NewIsActive = newActive;
            OldEndDate = oldEnd;
            NewEndDate = newEnd;
            IsNewSubscription = isNewSubscription;
        }

        public Models.AuthorPlans Plan { get; }
        public int? OldIsActive { get; }
        public int NewIsActive { get; }
        public DateTime? OldEndDate { get; }
        public DateTime NewEndDate { get; }
        public bool IsNewSubscription { get; }
    }
}
