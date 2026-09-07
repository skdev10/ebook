using EBookDashboard.Models;

namespace EBookDashboard.Models.ViewModels;

/// <summary>Dashboard Orders page: live book bills plus paid checkout receipts.</summary>
public sealed class OrdersPageViewModel
{
    public List<OpenBookBillRow> OpenBills { get; set; } = new();
    public List<Order> Receipts { get; set; } = new();
}

/// <summary>One manuscript with its current export estimate.</summary>
public sealed class OpenBookBillRow
{
    public int BookId { get; set; }
    public string Title { get; set; } = "";
    public int PageCount { get; set; }
    public int PaidPages { get; set; }
    public int FreeAllowance { get; set; }
    public decimal AmountDue { get; set; }
    public string Currency { get; set; } = "USD";
    public DateTime UpdatedAt { get; set; }
}
