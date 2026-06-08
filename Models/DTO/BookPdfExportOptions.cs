using System.Globalization;
using System.Text.Json;
using EBookDashboard.Models;
using EBookDashboard.Services;

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

    /// <summary>Formatter preview accent (#RRGGBB) — tints borders and highlights in PDF export.</summary>
    public string? PreviewAccent { get; set; }

    /// <summary>Optional page/sheet background (#RRGGBB). When null, uses interior-style default from formatter.</summary>
    public string? PageBackgroundColor { get; set; }

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
            if (TryGetString(root, "previewAccent", out s)) o.PreviewAccent = NormalizeHexColor(s);
            if (TryGetString(root, "pageBackgroundColor", out s)) o.PageBackgroundColor = NormalizeHexColor(s);
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

        o.Normalize();
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
        Normalize();
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

    public string BodyFontSizePt() =>
        InteriorExportTheme.ResolveBodyFontSizePt(InteriorStyle, TextSize);

    public string BodyLineHeight() =>
        InteriorExportTheme.ResolveLineHeight(LineSpacing);

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
        if (!string.IsNullOrWhiteSpace(req.PublishingPlatforms))
            PublishingPlatforms = req.PublishingPlatforms.Trim();
        if (!string.IsNullOrWhiteSpace(req.PreviewAccent))
            PreviewAccent = NormalizeHexColor(req.PreviewAccent);
        if (!string.IsNullOrWhiteSpace(req.PageBackgroundColor))
            PageBackgroundColor = NormalizeHexColor(req.PageBackgroundColor);
        Normalize();
    }

    /// <summary>Resolved page background for PDF — explicit override or interior-style sheet color.</summary>
    public string ResolvePageBackgroundColor() =>
        PageBackgroundColor ?? InteriorExportTheme.ResolveDefaultPageBackground(InteriorStyle);

    private static string? NormalizeHexColor(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        return System.Text.RegularExpressions.Regex.IsMatch(s, @"^#[0-9A-Fa-f]{6}$") ? s : null;
    }

    /// <summary>Overlay persisted formatter draft JSON — used as fallback; <see cref="BookFormatting"/> wins on export.</summary>
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
        Normalize();
    }

    /// <summary>Load export options from persisted formatter draft JSON + BookFormatting row (DB wins on conflicts).</summary>
    public static BookPdfExportOptions LoadFromPersistence(BookFormatting? formattingRow, string? draftJson)
    {
        var exportOpt = FromDraftJson(draftJson);
        exportOpt.MergeFromBookFormatting(formattingRow);
        return exportOpt;
    }

    /// <summary>Canonicalize interior style / text size / line spacing for export.</summary>
    public void Normalize()
    {
        InteriorStyle = InteriorExportTheme.NormalizeInteriorStyle(InteriorStyle);
        TextSize = InteriorExportTheme.NormalizeTextSize(TextSize);
        LineSpacing = InteriorExportTheme.NormalizeLineSpacing(LineSpacing);
        if (string.IsNullOrWhiteSpace(PublishingPlatform) && !string.IsNullOrWhiteSpace(PublishingPlatforms))
            PublishingPlatform = PublishingPlatforms.Split(',')[0].Trim();
    }
}
