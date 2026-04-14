namespace EBookDashboard.Interfaces
{
    public interface IChatService
    {
        Task<string> GetResponseAsync(string userMessage, CancellationToken cancellationToken = default);
    }

}

