using System.Text;
using EBookDashboard.Models;
using EBookDashboard.Services;
using Microsoft.AspNetCore.Mvc;

namespace EBookDashboard.Controllers;

public class CalculatorController : Controller
{
    private readonly ISpineCalculatorService _spine;

    public CalculatorController(ISpineCalculatorService spine)
    {
        _spine = spine;
    }

    [HttpGet]
    public IActionResult Index()
    {
        return View(new BookCoverModel { Pages = 150, PaperType = "White paper", Title = "My Book", Author = "Author Name" });
    }

    /// <summary>
    /// JSON API: GET /Calculator/Dimensions?pages=150&amp;paper=white
    /// </summary>
    [HttpGet]
    public IActionResult Dimensions(int pages = 150, string paper = "White paper")
    {
        var result = _spine.Calculate(pages, paper);
        return Json(result);
    }

    /// <summary>
    /// Downloads a plain-text spec sheet.
    /// </summary>
    [HttpGet]
    public IActionResult SpecSheet(int pages = 150, string paper = "White paper", string title = "My Book", string author = "Author")
    {
        var d = _spine.Calculate(pages, paper);
        var sb = new StringBuilder();
        sb.AppendLine("KDP Cover Specification");
        sb.AppendLine($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        sb.AppendLine("─────────────────────────");
        sb.AppendLine($"Title:        {title}");
        sb.AppendLine($"Author:       {author}");
        sb.AppendLine($"Book size:    6\" × 9\"");
        sb.AppendLine($"Pages:        {d.Pages}");
        sb.AppendLine($"Paper:        {d.PaperType}");
        sb.AppendLine($"Spine:        {d.SpineInches}\" / {d.SpineMm}mm");
        sb.AppendLine($"Total width:  {d.TotalWidthInches}\" / {d.TotalWidthMm}mm");
        sb.AppendLine($"Height:       {d.TotalHeightInches}\" / {d.TotalHeightMm}mm");
        sb.AppendLine($"Bleed:        {d.BleedInches}\" on all sides");
        sb.AppendLine($"Barcode area: 2.000\" × 1.200\" bottom-right of back cover");
        if (!string.IsNullOrEmpty(d.Warning))
            sb.AppendLine($"Warning:      {d.Warning}");

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/plain", $"kdp-cover-spec-{pages}p.txt");
    }
}
