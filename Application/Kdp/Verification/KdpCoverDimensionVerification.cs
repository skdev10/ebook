using EBookDashboard.Application.Kdp.Constants;
using EBookDashboard.Application.Kdp.DTOs;
using EBookDashboard.Application.Kdp.Services;

namespace EBookDashboard.Application.Kdp.Verification;

/// <summary>
/// Runnable KDP calculator examples — mirrors official Cover Calculator outputs.
/// Call <see cref="RunAll"/> from diagnostics or unit tests.
/// </summary>
public static class KdpCoverDimensionVerification
{
    public static IReadOnlyList<string> RunAll()
    {
        var failures = new List<string>();
        var service = new KdpCoverDimensionService();

        AssertExample(service, failures, "24-page official example", new KdpCalculateRequest
        {
            PageCount = 24,
            TrimWidth = 6m,
            TrimHeight = 9m,
            Dpi = 150,
            Bleed = true
        }, expected =>
        {
            Expect(failures, "fullCoverWidth", 12.304m, expected.FullCoverWidth);
            Expect(failures, "fullCoverHeight", 9.25m, expected.FullCoverHeight);
            Expect(failures, "spineWidth", 0.054m, expected.SpineWidth);
            Expect(failures, "pixelWidth", 1846, expected.PixelWidth);
            Expect(failures, "pixelHeight", 1388, expected.PixelHeight);
            Expect(failures, "spinePixels", 8, expected.SpinePixels);
            Expect(failures, "safeAreaWidth", 5.875m, expected.SafeAreaWidth);
            Expect(failures, "safeAreaHeight", 8.75m, expected.SafeAreaHeight);
            Expect(failures, "bleed", 0.125m, expected.Bleed);
        });

        AssertExample(service, failures, "100-page dynamic spine", new KdpCalculateRequest
        {
            PageCount = 100,
            TrimWidth = 6m,
            TrimHeight = 9m,
            Dpi = 150,
            Bleed = true
        }, expected =>
        {
            Expect(failures, "spineWidth", 0.225m, expected.SpineWidth);
            Expect(failures, "fullCoverWidth", 12.475m, expected.FullCoverWidth);
        });

        AssertExample(service, failures, "no bleed", new KdpCalculateRequest
        {
            PageCount = 24,
            TrimWidth = 6m,
            TrimHeight = 9m,
            Dpi = 150,
            Bleed = false
        }, expected =>
        {
            Expect(failures, "fullCoverWidth", 12.054m, expected.FullCoverWidth);
            Expect(failures, "fullCoverHeight", 9m, expected.FullCoverHeight);
            Expect(failures, "backPanelX", 0m, expected.BackPanelXInches);
            Expect(failures, "spineX", 6m, expected.SpineXInches);
            Expect(failures, "frontPanelX", 6.054m, expected.FrontPanelXInches);
        });

        var rate = KdpCoverDimensionService.ResolveSpineInchesPerPage(
            KdpPaperbackConstants.InteriorTypeStandardColor,
            KdpPaperbackConstants.PaperTypeWhite);
        Expect(failures, "spinePerPage", 0.002252m, rate);

        return failures;
    }

    private static void AssertExample(
        KdpCoverDimensionService service,
        List<string> failures,
        string name,
        KdpCalculateRequest request,
        Action<KdpCalculateResponse> assert)
    {
        try
        {
            var result = service.Calculate(request);
            assert(result);
        }
        catch (Exception ex)
        {
            failures.Add($"{name}: {ex.Message}");
        }
    }

    private static void Expect(List<string> failures, string field, decimal expected, decimal actual)
    {
        if (expected != actual)
            failures.Add($"{field}: expected {expected}, got {actual}");
    }

    private static void Expect(List<string> failures, string field, int expected, int actual)
    {
        if (expected != actual)
            failures.Add($"{field}: expected {expected}, got {actual}");
    }
}
