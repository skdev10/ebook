namespace EBookDashboard.Interfaces;

/// <summary>Fire-and-forget queue for background print wrap generation after front cover save.</summary>
public interface IPrintWrapPregenerationQueue
{
    /// <summary>Starts wrap generation on a background thread; does not block the caller.</summary>
    void QueueAfterFrontCoverSaved(int userId, int bookId);
}
