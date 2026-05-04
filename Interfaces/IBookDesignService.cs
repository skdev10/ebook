using EBookDashboard.Models;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Interfaces
{
    public interface IBookDesignService
    {

        Task<CoverDesignCalculator?> GetCoverDesignCalculator(int userId, int bookId);
        Task<IEnumerable<SavedBookCoverDesignDto>> GetSavedBooksCoversForDropdownAsync();

    }
}
