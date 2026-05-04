using EBookDashboard.Models;

namespace EBookDashboard.Interfaces
{
    public interface IAuthorBillsService
    {
        Task<AuthorBills?> GetBillByIdAsync(int billId);
        Task<AuthorBills?> GetRecentBillByUserAsync(int userId, string userEmail);
        Task<IEnumerable<AuthorBills>> GetBillsByAuthorAsync(int authorId);
        Task<bool> UpdateBillPaymentStatusAsync(int billId, string paymentReference, string status);
        Task<bool> CancelBillAsync(int billId, string cancellationReason);
        Task<IEnumerable<AuthorBills>> GetAuthorBillsAsync(int authorId);
        Task<AuthorBills> GetAuthorBillWithFeaturesAsync(int billId);

    }
}
