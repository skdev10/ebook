using EBookDashboard.Models;

namespace EBookDashboard.Services;

public interface ISpineCalculatorService
{
    CoverDimensionsResult Calculate(int pages, string paper);
}
