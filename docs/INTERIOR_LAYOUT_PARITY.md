# Interior Layout Parity — BookPreview ≡ PDF Export

## Source of truth

All numeric spacing lives in **`Services/InteriorSpacingTheme.cs`** (millimeters).  
**`Services/InteriorLayoutTokens.cs`** converts those values to CSS and emits identical custom properties for:

- Book Formatter preview (`BuildFormatterSyncCss`)
- Chromium PDF export (`InteriorExportTheme.BuildPdfThemeCss`)
- Client token sync (`BuildClientSpecsJson` → `syncInteriorTokenVars`)

Never hard-code margin/padding/line-height numbers in views, CSS files, or export builders.

## Trade paperback defaults (Novel / Traditional — 6×9)

| Token | Value | Purpose |
|-------|-------|---------|
| `PageTopPaddingMm` | 21.59 mm (0.85″) | Air below running head |
| `PageBottomPaddingMm` | 20.32 mm (0.80″) | Air above folio |
| `PageInsideMarginMm` | 19.05 mm (0.75″) | Binding gutter (left on recto) |
| `PageOutsideMarginMm` | 13.97 mm (0.55″) | Fore-edge |
| `TextColumnWidthMm` | 106.68 mm (4.2″) | ~65 CPL at 11–12 pt |
| `ChapterDropMm` | 26 mm | Chapter title vertical drop |
| `ParagraphSpacingMm` | 1.9 mm | Between paragraphs |
| `FirstLineIndentMm` | 6.35 mm | First-line indent |

## Line-height multipliers (exact)

| UI label | Multiplier |
|----------|------------|
| Tight | 1.4 |
| Normal / Medium | 1.6 |
| Relaxed | 1.8 |
| Loose | 2.0 |

Preview sets `--fmt-line-height` and `--ilt-body-lh`; PDF uses the same `--ilt-body-lh` string.

## Preview vs PDF font size

| Context | CSS variable | Reason |
|---------|--------------|--------|
| Formatter preview (screen) | `--ilt-body-px` (e.g. `16px`) | DOM measurement / pagination |
| PDF export (print) | `--ilt-body-pt` (e.g. `12pt`) | Print-native units; `px × 0.75 = pt` |

Both derive from `InteriorExportTheme.ResolveBodyFontSizePx` — numerically equivalent at 96 DPI.

## Audit fixes (this pass)

1. **Centralized mm config** — `InteriorSpacingTheme.cs` with documented constants.
2. **Novel margins** — aligned to Amazon trade paperback reference (0.85 / 0.8 / 0.75 / 0.55 in).
3. **Chapter drop** — `26mm` (was `1.85rem` ≈ 29.6 px — caused PDF/preview drift).
4. **PDF body font** — `.book-pdf-body` rules now use `--ilt-body-pt` only (was incorrectly inheriting `--ilt-body-px`).
5. **Removed duplicate wrap padding** — `.book-page-content-wrap` no longer stacks padding from shared sheet rules; per-interior `!important` padding applies once.
6. **Client JSON** — `BuildClientSpecsJson` adds `spacing` block; `syncInteriorTokenVars` syncs chapter drop, text max, folio gaps.

## Chapter drop tuning

Change `InteriorSpacingTheme.ChapterDropMm` to `24`, `26`, or `28` and rebuild — both preview and PDF pick up `--ilt-chapter-drop` automatically. Default: **26 mm** (premium hierarchy without excess whitespace).

## Maintaining parity for new page types

1. Add any new spacing constant to `InteriorSpacingTheme` (mm).
2. Expose it in `InteriorLayoutTokens.BuildCssCustomProperties`.
3. Reference only `var(--ilt-*)` in CSS builders — never literals.
4. If the formatter measures height in JS, include the same running-head/footer DOM in `renderFmtMeasureSandbox`.
5. Add a test in `ManuscriptExportPrepTests` asserting the token appears in both `BuildFormatterSyncCss` and `BuildPdfThemeCss`.

## KDP alignment

- **Trim:** 6×9 in (152.4 × 228.6 mm)
- **Gutter wider than fore-edge:** inside 0.75″ > outside 0.55″
- **Text measure:** 4.2″ column → 60–75 characters per line at Medium body size
- **Chromium @page margin box:** `KdpPrintMarginBox` reserves running-head/folio safe zone outside the padded sheet

## Dashboard alignment

Book flow screens share **`DashboardLayoutTokens`** (`docs/DASHBOARD_LAYOUT_TOKENS.md`):
background `#f5f7f9`, padding `32px` / `48px`, preview pane `68%`, column gap `2.75rem`.

## Validation checklist

- [ ] PUBG interior: Formatter preview pages 1–3 vs exported PDF pages 1–3 — margins, line-height, chapter title position match.
- [ ] 2–3 other books: no cramped top/bottom; consistent gutter.
- [ ] `dotnet test` — `Preview_and_pdf_css_share_identical_layout_custom_properties` passes.
- [ ] AI Writer, Formatter, Cover Design, Publish — same `--dash-page-bg` and aligned grid padding.
