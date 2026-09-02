using EBookDashboard.Models;
using EBookDashboard.Models.ViewModels;

namespace EBookDashboard.Interfaces;

/// <summary>Aggregates user-panel data so admin screens stay in sync with author workspaces.</summary>
public interface IAdminPanelSyncService
{
    /// <summary>Full user workspace (profile, plan, books, billing) matching the user dashboard.</summary>
    Task<AdminUserWorkspaceViewModel?> GetUserWorkspaceAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>Full book workspace (writer / format / cover / publish) matching user-panel screens.</summary>
    Task<AdminBookWorkspaceViewModel?> GetBookWorkspaceAsync(int bookId, CancellationToken cancellationToken = default);

    /// <summary>Adds flow step, chapter count, and cover flags to admin book list rows.</summary>
    Task EnrichBookRowsAsync(IList<BookManagementViewModel> books, CancellationToken cancellationToken = default);

    /// <summary>Adds active plan name onto admin user list rows.</summary>
    Task EnrichUserRowsAsync(IList<UserManagementViewModel> users, CancellationToken cancellationToken = default);
}
