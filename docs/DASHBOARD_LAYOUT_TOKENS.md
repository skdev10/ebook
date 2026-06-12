# Dashboard Layout Tokens

## Source of truth

**`Services/DashboardLayoutTokens.cs`** defines all shared dashboard flow values.  
**`wwwroot/css/dashboard-flow.css`** applies layout classes.  
**`Views/Shared/_DashboardLayout.cshtml`** injects CSS variables on every dashboard page.

## Standard values

| Token | Value | Purpose |
|-------|-------|---------|
| `PageBackground` | `#f5f7f9` | Canvas behind all flow screens |
| `SurfaceBackground` | `#ffffff` | Cards and panels |
| `FlowMaxWidthPx` | `1600` | Content max width |
| `FlowPaddingDesktopPx` | `48` | Main padding (lg) |
| `FlowColumnGapRem` | `2.75` | Gap between config + preview columns |
| `PreviewPaneWidthPercent` | `68` | Formatter preview pane width |
| `CardRadiusPx` | `14` | Card corner radius |

## CSS classes

| Class | Use |
|-------|-----|
| `.dash-flow-page` | Outer page wrapper (background) |
| `.dash-flow-main` | Scrollable main with standard padding |
| `.dash-flow-inner` | `max-width: 1600px` centered container |
| `.dash-flow-grid` | Two-column flow (480px + preview) |
| `.dash-flow-grid--writer` | AI Writer 42% / 58% split |
| `.dash-flow-preview-pane` | 68% preview column width |
| `.dash-flow-card` | White bordered card surface |

## Screens using tokens

- `Views/Books/AIGenerateBook.cshtml`
- `Views/BookDesign/CoverDesignCalculatorFixing.cshtml`
- `Views/Dashboard/CoverDesign.cshtml`
- `Views/Dashboard/Publish.cshtml`

## Adding a new dashboard screen

1. Wrap content in `.dash-flow-page` > `.dash-flow-main` > `.dash-flow-inner`.
2. Use `.dash-flow-grid` for two-column flows.
3. Do not hard-code `#f5f7f9` — use `var(--dash-page-bg)`.
4. Tune spacing in `DashboardLayoutTokens.cs` only.
