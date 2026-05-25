using EBookDashboard.Application.Kdp.Constants;
using EBookDashboard.Application.Kdp.DTOs;

namespace EBookDashboard.Application.Kdp.Validation;

public static class KdpCalculateRequestValidator
{
    public static IReadOnlyList<string> Validate(KdpCalculateRequest? request)
    {
        var errors = new List<string>();
        if (request == null)
        {
            errors.Add("Request body is required.");
            return errors;
        }

        if (request.PageCount < KdpPaperbackConstants.MinPageCount)
            errors.Add($"PageCount must be at least {KdpPaperbackConstants.MinPageCount} (KDP paperback minimum).");

        if (request.PageCount > KdpPaperbackConstants.MaxPageCount)
            errors.Add($"PageCount must not exceed {KdpPaperbackConstants.MaxPageCount} (KDP paperback maximum).");

        if (request.TrimWidth <= 0)
            errors.Add("TrimWidth must be greater than zero.");

        if (request.TrimHeight <= 0)
            errors.Add("TrimHeight must be greater than zero.");

        if (request.Dpi < 72)
            errors.Add("Dpi must be at least 72.");

        if (request.Dpi > 600)
            errors.Add("Dpi must not exceed 600.");

        return errors;
    }
}
