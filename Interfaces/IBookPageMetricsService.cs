using EBookDashboard.Models.DTO;

namespace EBookDashboard.Interfaces;

public interface IBookPageMetricsService
{
    BookPageMetricsDto Estimate(BookDetailsResponseDto? details, BookPdfExportOptions? options);
}
