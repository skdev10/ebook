namespace EBookDashboard.Interfaces;

/// <summary>Billing page count from word-count proxy. Never reads rendered PDF pages.</summary>
public interface IPageCountService
{
    Task<int> GetBillablePageCountAsync(int bookId);
    Task<int> GetWordCountAsync(int bookId);
}
