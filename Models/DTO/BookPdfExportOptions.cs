using System.Globalization;
using System.Text.Json;
using EBookDashboard.Models;

namespace EBookDashboard.Models.DTO;

/// <summary>Interior options for PDF export — mirrors formatter draft JSON (camelCase) and BookFormatting rows.</summary>
public class BookPdfExportOptions
{
    public bool IncludeCoverPage { get; set; } = true;
    public string InteriorStyle { get; set; } = "Novel";
    public string TextSize { get; set; } = "Medium";
    public string LineSpacing { get; set; } = "1.6";
    public string Format { get; set; } = "Ebook";

    /// <summary>Primary distributor for trim, margins, and print notes (e.g. Amazon KDP).</summary>
    public string PublishingPlatform { get; set; } = "";

    /// <summary>Comma-separated list from formatter (used when <see cref="PublishingPlatform"/> is empty).</summary>
    public string? PublishingPlatforms { get; set; }

    public static BookPdfExportOptions FromDraftJson(string? json)
    {
        var o = new BookPdfExportOptions();
        if (string.IsNullOrWhiteSpace(json)) return o;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (TryGetString(root, "interiorStyle", out var s)) o.InteriorStyle = s;
            if (TryGetString(root, "textSize", out s)) o.TextSize = s;
            if (TryGetString(root, "lineSpacing", out s)) o.LineSpacing = s;
            if (TryGetString(root, "format", out s)) o.Format = NormalizeFormatToken(s);
            if (TryGetString(root, "publishingPlatform", out s)) o.PublishingPlatform = s;
            if (TryGetString(root, "publishingPlatforms", out s)) o.PublishingPlatforms = s;
            if (root.TryGetProperty("includeCoverPage", out var c))
            {
                if (c.ValueKind == JsonValueKind.True) o.IncludeCoverPage = true;
                else if (c.ValueKind == JsonValueKind.False) o.IncludeCoverPage = false;
            }
        }
        catch
        {
            /* use defaults */
        }

        return o;
    }

    /// <summary>
    /// Applies persisted <see cref="BookFormatting"/> over draft JSON so exports match saved settings.
    /// </summary>
    public void MergeFromBookFormatting(BookFormatting? f)
    {
        if (f == null) return;
        if (!string.IsNullOrWhiteSpace(f.InteriorStyle)) InteriorStyle = f.InteriorStyle.Trim();
        if (!string.IsNullOrWhiteSpace(f.TextSize)) TextSize = f.TextSize.Trim();
        if (!string.IsNullOrWhiteSpace(f.LineSpacing)) LineSpacing = f.LineSpacing.Trim();
        if (!string.IsNullOrWhiteSpace(f.Format)) Format = NormalizeFormatToken(f.Format);
        PublishingPlatforms = f.PublishingPlatforms;
        if (!string.IsNullOrWhiteSpace(f.PublishingPlatform)) PublishingPlatform = f.PublishingPlatform.Trim();
        if (string.IsNullOrWhiteSpace(PublishingPlatform) && !string.IsNullOrWhiteSpace(PublishingPlatforms))
            PublishingPlatform = PublishingPlatforms.Split(',')[0].Trim();
    }

    public string PrimaryPlatformToken()
    {
        if (!string.IsNullOrWhiteSpace(PublishingPlatform)) return PublishingPlatform.Trim();
        if (string.IsNullOrWhiteSpace(PublishingPlatforms)) return "";
        return PublishingPlatforms.Split(',')[0].Trim();
    }

    private static string NormalizeFormatToken(string s)
    {
        var t = (s ?? "").Trim();
        if (t.Equals("Print", StringComparison.OrdinalIgnoreCase)) return "Paperback";
        return string.IsNullOrEmpty(t) ? "Ebook" : t;
    }

    private static bool TryGetString(JsonElement root, string name, out string value)
    {
        value = "";
        if (!root.TryGetProperty(name, out var p)) return false;
        value = p.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(value);
    }

    public string BodyFontSizePt()
    {
        if (string.Equals(TextSize, "Small", StringComparison.OrdinalIgnoreCase)) return "10.5";
        if (string.Equals(TextSize, "Large", StringComparison.OrdinalIgnoreCase)) return "15";
        return "12";
    }

    public string BodyLineHeight()
    {
        if (!double.TryParse(LineSpacing, NumberStyles.Any, CultureInfo.InvariantCulture, out var lh))
            lh = 1.55;
        if (lh < 1.15) lh = 1.15;
        if (lh > 2.4) lh = 2.4;
        return lh.ToString("0.###", CultureInfo.InvariantCulture);
    }

    /// <summary>Apply client-sent formatter snapshot (Publish localStorage) over DB-loaded options.</summary>
    public void ApplyRequestOverrides(ExportBookPdfRequest? req)
    {
        if (req == null) return;
        if (!string.IsNullOrWhiteSpace(req.InteriorStyle)) InteriorStyle = req.InteriorStyle.Trim();
        if (!string.IsNullOrWhiteSpace(req.TextSize)) TextSize = req.TextSize.Trim();
        if (!string.IsNullOrWhiteSpace(req.LineSpacing)) LineSpacing = req.LineSpacing.Trim();
        if (!string.IsNullOrWhiteSpace(req.BookFormat)) Format = NormalizeFormatToken(req.BookFormat);
        if (!string.IsNullOrWhiteSpace(req.PublishingPlatform))
            PublishingPlatform = req.PublishingPlatform.Trim();
    }

    /// <summary>Overlay persisted formatter draft JSON on top of saved BookFormatting (draft wins when set).</summary>
    public void OverlayFromDraftJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return;
        var draft = FromDraftJson(json);
        if (!string.IsNullOrWhiteSpace(draft.InteriorStyle)) InteriorStyle = draft.InteriorStyle.Trim();
        if (!string.IsNullOrWhiteSpace(draft.TextSize)) TextSize = draft.TextSize.Trim();
        if (!string.IsNullOrWhiteSpace(draft.LineSpacing)) LineSpacing = draft.LineSpacing.Trim();
        if (!string.IsNullOrWhiteSpace(draft.Format)) Format = draft.Format;
        if (!string.IsNullOrWhiteSpace(draft.PublishingPlatform)) PublishingPlatform = draft.PublishingPlatform.Trim();
        if (!string.IsNullOrWhiteSpace(draft.PublishingPlatforms)) PublishingPlatforms = draft.PublishingPlatforms;
    }
}
