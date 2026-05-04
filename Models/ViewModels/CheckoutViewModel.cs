namespace EBookDashboard.Models.ViewModels
{
    public class CheckoutViewModel
    {
        // AuthorBills table
        public AuthorBills? Bill { get; set; }
        // AuthorPlans table
        public AuthorPlans? Plan { get; set; }
        public string Type { get; set; } = "plan"; // "plan" or "features"

        // Helper properties for the view
        public decimal TotalAmount =>
                (decimal?)(Bill?.TotalAmount) ??
                (decimal?)(Plan?.PlanRate) ?? 0;
        //public decimal TotalAmount => Bill?.TotalAmount ?? Plan?.PlanRate ?? 0;
        public string Description => Bill?.Description ?? Plan?.PlanName ?? "Checkout";
        public string Currency => Bill?.Currency ?? Plan?.Currency ?? "usd";
    }
}
