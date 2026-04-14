using EBookDashboard.Models;
using EBookDashboard.Services;
using Newtonsoft.Json;
using System.Net;

namespace EBookDashboard.Interfaces
{
    public interface IAPIRawResponseService
    {
        Task<int> SaveRawResponseAsync(AIBookRequest request, string responseData, string endpoint, string statusCode, string? errorMessage = null);
        Task<Models.APIRawResponse> GetRawResponseAsync(int responseId);
        Task<int> UpdateRawResponseAsync(AIBookRequest request, string responseData, string endpoint, string statusCode, string? errorMessage = null);
        Task<IEnumerable<APIRawResponse>> GetRawResponsesByUserAndBookAsync(int userId, int bookId, int responseId);
        Task<APIRawResponse?> GetLatestRawResponseAsync(int userId, int bookId);
        Task<APIRawResponse?> GetRawResponseByChapterAsync(int userId, int bookId, int chapter);
        Task<IEnumerable<APIRawResponse>> GetChaptersFromRawResponsesAsync(int userId, int bookId);
        Task<bool> RawResponsesExistAsync(int userId, int bookId);
        Task<int> GetRawResponseCountAsync(int userId, int bookId);
        Task<int?> GetLatestResponseIdAsync(int userId, int bookId);
        Task<int> SaveRawResponseEditAsync(AIBookRequestEdit request, string responseData, string endpoint, string statusCode, string? errorMessage = null);
        Task<List<APIRawResponse>> GetChaptersByBookAsync(int bookId, int userId);
        Task<APIRawResponse?> GetChapterAsync(int responseId);
    }
}
