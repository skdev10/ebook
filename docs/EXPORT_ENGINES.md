# Export Engine Audit — EBookDashboard

**Date:** 2026-06-12  
**Conclusion:** One authoritative HTML document feeds preview + PDF. **PuppeteerSharp (Chromium)** is the primary PDF engine. **Custom ZIP EPUB** shares chapter HTML. **PDFsharp** is offline fallback only.

---

## Libraries searched

| Library | Found? | Role |
|---------|--------|------|
| **PuppeteerSharp** | ✅ `newEbook.csproj` v20.2.4 | **Primary PDF** — `ChromiumPdfExporter` renders `BookPreviewPrintHtmlBuilder` HTML |
| **PDFsharp** | ✅ v6.2.0 | **Fallback PDF** only when Chromium fails or `PdfExport:Engine=PdfSharp` |
| QuestPDF | ❌ | Not referenced |
| iTextSharp / iText7 | ❌ (comment only in BooksController) | Not used |
| wkhtmltopdf | ❌ (jQuery comment only) | Not used |
| DinkToPdf | ⚠️ enum only | `PdfExportEngine.DinkToPdf` → logs warning, falls back to Chromium |
| HtmlRenderer | ❌ | Not used (legacy comment in PromptTemplates.cs) |
| EpubSharp / VersOne.Epub | ❌ | Not used |
| IronPDF / SelectPdf / Syncfusion | ❌ | Not integrated |

---

## PDF paths (before unification)

| Path | File | Status |
|------|------|--------|
| **Canonical HTML → Chromium** | `BookPdfService` → `BookPreviewPrintHtmlBuilder` → `ChromiumHtmlPdfExportService` | ✅ **Authoritative** |
| PdfSharp structured fallback | `PdfSharpBookExporter`, `BookPdfSharpRenderer` | ⚠️ Secondary — different layout if Chromium unavailable |
| Stored HTML fragment | `RenderStoredBookHtmlPdfAsync` | Legacy AI Writer path |
| Mock PDF text file | `BooksController.GenerateFormats` | ❌ **Removed** — was fake `WriteAllTextAsync("PDF MOCK")` |

---

## EPUB path

| Component | File | Notes |
|-----------|------|-------|
| **EPUB builder** | `EpubExportService.cs` | Manual OCF/ZIP; no third-party EPUB lib |
| Chapter HTML | `BookManuscriptHtmlFormatter` + `InteriorExportTheme.BuildEpubStylesheet` | Same manuscript pipeline as PDF |
| Typography | `InteriorExportTheme.ResolveBodyFontSizePx` | Matches formatter `applyPreviewStyles` |

EPUB is reflowable — fixed page breaks differ from PDF, but fonts/sizes/line-height match user settings.

---

## Python backend (`162.229.248.26:8001`)

Used **only** for AI generation (chapters, covers, audio, queue). **Not** used for PDF/EPUB export.

---

## Single source of truth (implemented)

```
BookFormatting (DB) + BookPdfExportOptions
        ↓
InteriorLayoutTokens + InteriorExportTheme  →  dynamic CSS
        ↓
BookRenderService.BuildBookHtmlAsync()
        ↓
   ┌────┴────┐
   ↓         ↓
Preview     ChromiumPdfExporter → PDF bytes (in-memory)
(iframe)    EpubExportService  → EPUB bytes (in-memory)
```

- **Preview:** `GET /BookDesign/PreviewBookHtml?bookId=` returns the same HTML as PDF input.
- **Download:** Controllers return `File(byte[], mime, filename)` — no disk temp path on the hot path.
- **First-click fix:** Chromium warm-up on startup + client retry; generation fully awaited before `FileResult`.

---

## Configuration

```json
"PdfExport": { "Engine": "Chromium" },
"Puppeteer": { "ExecutablePath": "/usr/bin/google-chrome-stable" }
```

Server: `apt install google-chrome-stable` (see `docs/PDF_EXPORT.md`).

If Chromium cannot run on the droplet, set `PdfExport:Engine` to `PdfSharp` — output will **not** match HTML preview pixel-for-pixel (documented limitation).

---

## Files to know

| File | Purpose |
|------|---------|
| `Services/BookRenderService.cs` | `BuildBookHtmlAsync` — shared HTML |
| `Services/PdfExport/BookPreviewPrintHtmlBuilder.cs` | Full print document |
| `Services/InteriorLayoutTokens.cs` | KDP margins, padding, typography tokens |
| `Services/InteriorExportTheme.cs` | PDF + EPUB theme CSS |
| `Services/PdfExport/ChromiumPdfExporter.cs` | Headless print |
| `Services/EpubExportService.cs` | EPUB ZIP |
| `Services/PdfExport/PdfSharpBookExporter.cs` | Offline fallback |
