using EBookDashboard.Infrastructure;
using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace EBookDashboard.Controllers.Api;

[ApiController]
[Route("api/cover")]
[Authorize]
public class CoverApiController : ControllerBase
{
    private readonly CoverCalculator _calc;
    private readonly CoverConformer _conformer;

    public CoverApiController(CoverCalculator calc, CoverConformer conformer)
    {
        _calc = calc;
        _conformer = conformer;
    }

    [HttpPost("calculate")]
    [AllowAnonymous]
    public ActionResult<CoverResult> Calculate([FromBody] CoverRequest req) => Ok(_calc.Calculate(req));

    /// <summary>
    /// Called when the user publishes. Accepts the wraparound as PNG file or base64.
    /// Resizes to exact KDP dimensions — does not slice into panels.
    /// </summary>
    [HttpPost("publish")]
    public async Task<IActionResult> Publish([FromForm] CoverRequest req,
        IFormFile? wraparound, [FromForm] string? encodedImage, [FromForm] int bookId = 0)
    {
        // Clean print-ready cover bytes are paywalled when tied to a book.
        if (bookId > 0)
        {
            var gate = await ExportGating.RequirePaidJsonAsync(HttpContext, bookId, paperback: true, hardcover: false);
            if (gate != null) return gate;
        }

        Stream stream;
        if (wraparound is not null &&
            (wraparound.ContentType == "image/png" ||
             Path.GetExtension(wraparound.FileName).Equals(".png", StringComparison.OrdinalIgnoreCase)))
        {
            stream = wraparound.OpenReadStream();
        }
        else if (!string.IsNullOrWhiteSpace(encodedImage))
        {
            var raw = encodedImage.Contains(',') ? encodedImage[(encodedImage.IndexOf(',') + 1)..] : encodedImage;
            stream = new MemoryStream(Convert.FromBase64String(raw));
        }
        else
        {
            return BadRequest("Provide the wraparound cover as a PNG file or base64 'encodedImage'.");
        }

        using (stream)
        {
            var (image, dims) = _conformer.Conform(req, stream);
            using var ms = new MemoryStream();
            await image.SaveAsPngAsync(ms, new PngEncoder());
            image.Dispose();

            Response.Headers["X-Cover-Width-Px"] = dims.FullCoverWidthPx.ToString();
            Response.Headers["X-Cover-Height-Px"] = dims.FullCoverHeightPx.ToString();
            return File(ms.ToArray(), "image/png", "kdp_cover_print_ready.png");
        }
    }
}
