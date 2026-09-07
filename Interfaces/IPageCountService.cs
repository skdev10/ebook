namespace EBookDashboard.Interfaces;

/// <summary>
/// Billing page count: measured writer/formatter layout pages when saved,
/// otherwise a word-count estimate from the merged manuscript.
/// </summary>
public interface IPageCountService
{
    Task<int> GetBillablePageCountAsync(int bookId);
    Task<int> GetWordCountAsync(int bookId);
}
