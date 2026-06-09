# EbookAI workflow: editing, unsaved changes, and PDF export

This document describes how chapter editing, navigation guards, preview, and PDF export fit together.

## End-to-end pipeline

```
AI generate chapter → User edits (AI Writer) → Autosave to DB
        → Book Formatting (interior style, margins) → Autosave to DB
        → Cover design → Publish / Export
        → BookPreview HTML (shared builder) → IPdfHtmlExportService → PDF download
```

### Shared HTML (preview = PDF)

| Component | Role |
|-----------|------|
| `BookManuscriptHtmlFormatter` | Chapter body → styled HTML |
| `ChapterContentNormalizer` | Strips API JSON wrappers from stored content |
| `InteriorPrintDocumentBuilder` | TOC, copyright, chapter sections |
| `BookPreviewPrintHtmlBuilder` | Full print document (same CSS as BookPreview) |
| `InteriorExportTheme` | Fonts, page color, heading keep-with-next |

BookPreview in the browser and exported PDF both use HTML produced by `BookPreviewPrintHtmlBuilder`.

## Unsaved changes guard

**Script:** `wwwroot/js/unsaved-changes-guard.js` (loaded in `_DashboardLayout.cshtml`)

**API:**

| Method | When |
|--------|------|
| `EbookUnsavedGuard.setDirty()` | User edits text, formatting, or form fields |
| `EbookUnsavedGuard.markSaved()` | Server returns success (autosave / explicit save) |
| `EbookUnsavedGuard.setContext('AI Writer')` | Page label in dialogs |
| `EbookUnsavedGuard.setPendingSaveHandler(fn)` | Optional “Save first” in leave dialog |

**Browser back / refresh / tab close**

- `beforeunload` runs **only** when `isDirty() === true`.
- Native browser message: “Changes you made may not be saved.”

**In-app navigation (sidebar, links)**

- Custom SweetAlert: **Stay on page** | **Save first** | **Leave without saving**
- Workflow “Go back” buttons (`confirmFlowBack`) check unsaved edits first, then show a **workflow reset** dialog (formatting/cover reset — chapter text in AI Writer is kept).

**Pages wired today**

- AI Writer (`AIGenerateBook.cshtml`) — inputs, preview `input` events, autosave to `/Books/SaveChapterContent`
- Book Formatting (`CoverDesignCalculatorFixing.cshtml`) — autosave to `/BookDesign/SaveBookFormatting`; local draft still written on `beforeunload` as recovery only (no extra warning)

## PDF export engine

**Configuration** (`appsettings.json`):

```json
"PdfExport": {
  "Engine": "Chromium",
  "Margins": { "Trim6x9": { ... } }
}
```

| Engine value | Implementation | Notes |
|--------------|----------------|-------|
| `Chromium` (default) | `ChromiumHtmlPdfExportService` + PuppeteerSharp | Full CSS, Google Fonts, cream page background |
| `PdfSharp` | Structured `PdfSharpBookExporter` fallback | No HTML; used if Chromium fails or engine forced |
| `DinkToPdf` | Reserved | Logs warning; falls back to Chromium |

**Abstraction**

- `IPdfHtmlExportService` — `ExportHtmlAsync(PdfHtmlExportRequest)`
- `PdfHtmlExportServiceResolver` — picks engine from config
- `BookPdfService` — builds HTML once, calls `IPdfHtmlExportService`, then PdfSharp fallback

See also: [PDF_EXPORT.md](./PDF_EXPORT.md)

## Rich text editor

Chapter content is edited in the **paginated BookPreview viewport** (`#preview-content`) with autosave to the server. A separate CKEditor/TinyMCE install is optional future work; the current pipeline already outputs HTML suitable for preview and PDF.

To add a dedicated RTE later:

1. Editor HTML → same `SaveChapterContent` endpoint  
2. `ChapterContentNormalizer` on read  
3. No change to PDF path if HTML matches preview classes  

## KDP checklist (6×9 interior)

- Trim: 6×9 in (`BookPdfLayoutOptions` / `PdfExport:Margins:Trim6x9`)
- Resolution: 300 DPI equivalent via Chromium print
- Page background: cream (`InteriorExportTheme`)
- Safe zone: 0.25" from trim (formatter + export CSS)
- Heading keep-with-next: shared JS + `InteriorExportTheme.BuildHeadingKeepWithNextCss()`

## Swapping PDF engine later

1. Implement `IPdfHtmlExportService` (e.g. `SyncfusionHtmlPdfExportService`).
2. Register in `PdfHtmlExportServiceResolver.ResolvePrimary()`.
3. Set `PdfExport:Engine` in config.
4. Run `tests/PdfSmokeTest.cs` and a manual BookPreview vs PDF compare.

## Operational notes

- **Chromium on server:** install Google Chrome and/or set `Puppeteer:ExecutablePath`.
- **No silent loss:** unsaved flag clears only after HTTP success; warnings match real state.
- **Workflow reset ≠ chapter delete:** going back from Format/Cover resets formatting/cover metadata, not AI Writer chapters already saved.
