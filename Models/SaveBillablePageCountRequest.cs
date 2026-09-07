namespace EBookDashboard.Models;

/// <summary>Persist the measured writer/formatter page count used for the writing bill.</summary>
public sealed class SaveBillablePageCountRequest
{
    public int BookId { get; set; }
    public int PageCount { get; set; }
}
