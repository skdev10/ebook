using EBookDashboard.Models;
using EBookDashboard.Models.DTO;

namespace EBookDashboard.Interfaces
{
    public interface IAudioToTextService
    {
        Task<string> ConvertAsync(AudioToTextRequest request);
    }
}
