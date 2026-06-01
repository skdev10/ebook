# EBook AI Platform — Upstream API Reference

**Version:** 1.2 · **Last updated:** Jun 2026  
**Live upstream base URL:** `http://162.229.248.26:8001`  
**ASP.NET BFF (production):** `http://138.197.76.70:5000`

This document is the **single source of truth** for integrators and senior developers working on the book-generation pipeline: chapters, audio, covers, print-ready wrap (back + spine + front), queue monitoring, and upstream MySQL tables.

---

## Table of contents

1. [Architecture](#architecture)
2. [Authentication](#authentication)
3. [Endpoint index](#endpoint-index)
4. [Chapter workflow](#chapter-workflow)
5. [Cover workflow](#cover-workflow)
6. [Print wrap cover (spine + back + front)](#print-wrap-cover-spine--back--front)
7. [Queue & diagnostics](#queue--diagnostics)
8. [Upstream database schema](#upstream-database-schema)
9. [ASP.NET BFF route map](#aspnet-bff-route-map)
10. [Configuration & secrets](#configuration--secrets)
11. [cURL cookbook](#curl-cookbook)
12. [Troubleshooting](#troubleshooting)

---

## Architecture

```mermaid
flowchart LR
  Browser[Browser / Dashboard UI]
  BFF[ASP.NET BFF\n138.197.76.70:5000]
  API[Python FastAPI\n162.229.248.26:8001]
  DB[(Upstream MySQL)]

  Browser --> BFF
  BFF -->|X-API-Key| API
  API --> DB
```

| Layer | Role |
|-------|------|
| **Upstream API** | AI generation, transcription, queue, persistence to `Temporary_database` / `User_confirm` |
| **ASP.NET BFF** | Auth session, payload normalization, KDP spine math, cover asset persistence, PDF/EPUB export |
| **Client export** | Optional client-side recalibration via `cover-kdp-export.js` for exact 300 DPI spine width from `page_count` |

---

## Authentication

Every upstream request **must** include:

```http
X-API-Key: YOUR_EXTERNAL_API_KEY
Content-Type: application/json
```

| Setting | Where to set |
|---------|----------------|
| `ExternalApi__ApiKey` | Linux: `/etc/default/ebookai`, DigitalOcean env, or `dotnet user-secrets` |
| Header name | **`X-API-Key`** (not `Authorization: Bearer`) |

> **Security:** Never commit API keys to git, chat, or screenshots. Configure only via environment variables or gitignored `appsettings.Local.json`. If a key was exposed, **revoke and rotate** it on GitHub / upstream immediately.

**Verify BFF key is loaded (after deploy):**

```bash
curl -s http://127.0.0.1:5000/Books/ExternalApiStatus
# Expect: "keyConfigured": true
```

---

## Endpoint index

| # | Method | Path | Purpose |
|---|--------|------|---------|
| 1 | POST | `/api/generate_chapter` | Generate chapter from user prompt |
| 2 | POST | `/api/edit` | Edit chapter via natural-language `changes` |
| 3 | POST | `/api/audio` | Transcribe audio → text |
| 4 | POST | `/api/approve` | Confirm chapter → `User_confirm` table |
| 5 | GET | `/api/queue-data` | Queue: running / waiting / max concurrent |
| 6 | POST | `/api/generate-cover` | Single front cover (eBook / preview) |
| 7 | POST | `/api/generate-spine-book-cover` | **Full print wrap:** back + spine + front |
| 8 | POST | `/api/edit-cover` | Edit cover from base64 + prompt |
| 9 | POST | `/api/book_chapters_name` | Suggest chapter names from highlights |
| 10 | POST | `/api/refine_cover_prompt` | Refine user cover prompt text |
| 11 | POST | `/api/suggest-cover-prompt-from-highlights` | Suggest cover prompt from book highlights |

**Cover constants (upstream validation):**

| Constant | Allowed values |
|----------|----------------|
| `VALID_SIZES` | `1024x1024`, `1536x1024`, `1024x1536`, `auto` |
| `VALID_QUALITIES` | `low`, `medium`, `high`, `auto` |

---

## Chapter workflow

### 1. Generate chapter

**`POST /api/generate_chapter`**

**Request**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "user_input": "how gravity descover"
}
```

| Field | Type | Notes |
|-------|------|-------|
| `user_id` | string | User identifier |
| `book_id` | string | Book identifier |
| `chapter` | string \| int | Chapter index |
| `user_input` | string | Generation prompt |

**Example result (heading-only shape):**

```json
{
  "data": {
    "heading": "The gravitational force is invented in 8790"
  }
}
```

The ASP.NET BFF normalizes multiple upstream shapes to `data.content` for the UI (`UpstreamResponseParser`), including:

- `data.content`
- `data.heading`
- root `heading`
- `data.suggest_chapter_name` (chapter-name suggestion list)

**Persists to:** `Temporary_database`  
**BFF route:** `POST /Books/AIGenerateBook`

---

### 2. Edit chapter

**`POST /api/edit`**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "In the heading, replace 8790 with 6789"
}
```

Use clear, natural-language `changes`. Example: user generated heading *"The gravitational force is invented in 8790"* and wants year **6789** instead.

**BFF routes:** `POST /Books/AIEditBook`, `POST /Books/EditChapter`

---

### 3. Audio transcription

**`POST /api/audio`**

**Supported formats:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

**JSON + server-local path** (path must exist on the **upstream worker** machine):

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 14,
  "audio_file_path": "c:\\book_project\\Audio_transcribe_into_text\\John s Morning Routi.mp3"
}
```

**Multipart (recommended for browser / ASP.NET):**

- Form fields: `user_id`, `book_id`, `chapter`
- File field: `audio`, `audio_file`, or `file`

**Persists to:** `audio_transcriptions`  
**BFF routes:** `POST /api/AudioToText/convert`, `POST /Audio/Upload`

---

### 4. Approve (confirm) chapter

**`POST /api/approve`**

Use **strict JSON** — no trailing spaces in property names:

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "approve": true
}
```

| Invalid | Why |
|---------|-----|
| `"chapter "` (trailing space) | Upstream may ignore unknown keys |
| `approve: True` (Python syntax) | Must be JSON `true` |

**Persists to:** `User_confirm`  
**BFF route:** `POST /Books/FinalizeChapterAPI`

---

## Cover workflow

### 5. Generate front cover (eBook)

**`POST /api/generate-cover`**

```json
{
  "title": "The Power of Gravity",
  "author_name": "Hasan Rahim",
  "category": "Science",
  "cover_style": "Modern Illustration",
  "size": "1024x1536",
  "quality": "medium"
}
```

**BFF routes:** `POST /Books/GenerateAICoverPreview`, `POST /Dashboard/GenerateCover`

---

### 6. Edit cover

**`POST /api/edit-cover`**

```json
{
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA...",
  "image_direction": "Make the sky brighter and add subtle stars",
  "size": "1024x1536"
}
```

| Field | Description |
|-------|-------------|
| `encoded_image` | Raw base64 PNG/JPEG (no `data:` prefix unless upstream documents otherwise) |
| `image_direction` | Natural-language edit instructions |
| `size` | One of `VALID_SIZES` |

**BFF routes:** `POST /Books/EditAICoverPreview`, `POST /Dashboard/EditCover`

---

### 7. Chapter name suggestions

**`POST /api/book_chapters_name`**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "highlights": [
    {
      "chapter": 1,
      "summary": "Hook: protagonist discovers anomaly."
    }
  ]
}
```

Alternative highlight shape:

```json
{
  "highlights": [
    {
      "chapter_name": "Chapter 1",
      "detailed_bullet_summary": "Opening scene establishes the world."
    }
  ]
}
```

Upstream may return ~5 names in `Temporary_database.suggest_chapter_name`; user picks one → stored as `chapter_name`.

**BFF route:** `POST /Books/SuggestChapterNames`

---

## Print wrap cover (spine + back + front)

### `POST /api/generate-spine-book-cover`

Generates a **full KDP paperback spread**: `[ back cover | spine | front cover ]` with bleed.

This is the endpoint used for **Peter Pan**-style Victorian ornamental wraps where the spine is a **thin vertical strip with no text** (see reference output below).

#### Minimal request (direct upstream)

```json
{
  "title": "Peter Pan",
  "author_name": "J. M. Barrie",
  "category": "Children's Fantasy",
  "cover_style": "Victorian ornamental",
  "size": "1536x1024",
  "quality": "high",
  "Interior_trim_size": "6 x 9 in",
  "page_count": 40,
  "paper_type": "white"
}
```

#### Full request (ASP.NET BFF — recommended)

When calling via `POST /Dashboard/GeneratePrintReadyCover`, the BFF adds KDP-calculated dimensions:

```json
{
  "title": "Peter Pan",
  "author_name": "J. M. Barrie",
  "category": "Children's Fantasy",
  "cover_style": "Victorian ornamental — deep navy, gold filigree, no spine text",
  "size": "1536x1024",
  "quality": "high",
  "Interior_trim_size": "6 x 9 in",
  "page_count": 40,
  "binding_type": "Paperback",
  "paper_type": "White paper",
  "interior_type": "Black & white",
  "spine_width_inches": 0.090,
  "spine_width_mm": 2.29,
  "wrap_width_inches": 12.340,
  "wrap_height_inches": 9.250,
  "wrap_width_mm": 313.44,
  "wrap_height_mm": 234.95,
  "bleed_inches": 0.125,
  "wrap_margin_inches": 0.125,
  "hinge_gap_inches": 0,
  "panel_width_inches": 6.0,
  "panel_height_inches": 9.0
}
```

#### Request fields

| Field | Required | Description |
|-------|----------|-------------|
| `title` | Yes | Book title (front cover) |
| `author_name` | Yes | Author on front cover |
| `category` | Yes | Genre / category for art direction |
| `cover_style` | Yes | Art direction prompt (style, palette, ornaments) |
| `size` | Yes | Image size — use `1536x1024` for landscape wrap |
| `quality` | Yes | `low` \| `medium` \| `high` \| `auto` |
| `Interior_trim_size` | Yes | e.g. `"6 x 9 in"` |
| `page_count` | Yes | **Drives spine width** — must match formatted manuscript |
| `paper_type` | Recommended | `"white"`, `"White paper"`, `"cream"`, etc. |
| `spine_width_inches` | BFF adds | Pre-calculated spine (see formula below) |
| `binding_type` | BFF adds | Usually `"Paperback"` |

#### KDP spine width formula (white paper)

```
spine_inches = page_count × 0.002252
```

| Pages | Spine (white) | Notes |
|-------|---------------|-------|
| 40 | **0.090"** (2.29 mm) | Reference KDP template |
| 79+ | wider | KDP allows spine text; **this product exports spine without text** |
| 250 | 0.563" | Longer books |

Full wrap width (paperback, bleed on):

```
wrap_width = 2 × trim_width + spine + 2 × 0.125"
wrap_height = trim_height + 2 × 0.125"
```

For **6×9**, 40 pages, white: **12.340" × 9.250"** (313.44 × 234.95 mm).

#### Expected visual output

| Panel | Content |
|-------|---------|
| **Back (left)** | Matching ornamental frame; barcode-safe lower area; no required text |
| **Spine (center)** | **Thin solid strip — NO title, NO author, NO text** |
| **Front (right)** | Title, author, full cover art inside gold frame |

Reference: *Peter Pan* — Victorian ornamental, navy + gold, narrow spine, zero spine typography.

#### Response

Upstream returns image URL(s) and/or base64. The BFF extracts:

| Asset | Setting key |
|-------|-------------|
| Full wrap | `book:{id}:printReadyCoverWrap` |
| Front | `book:{id}:printReadyCoverFront` |
| Back | `book:{id}:printReadyCoverBack` |
| Spine | `book:{id}:printReadyCoverSpine` |
| Page count | `book:{id}:printReadyPageCount` |

**Client-side export (300 DPI PNG):**  
`POST /Dashboard/GetPrintReadyCoverAssets` → `CoverKdpExport.composePrintWrapFromParts()` recalibrates spine to exact `page_count`, solid spine color, **no spine text**.

**BFF route:** `POST /Dashboard/GeneratePrintReadyCover`

---

## Queue & diagnostics

### `GET /api/queue-data`

No body. Returns queue metrics, e.g.:

```json
{
  "status_running": 2,
  "status_waiting": 5,
  "status_max_concurrent": 4,
  "status_total_requests": 128
}
```

**BFF route:** `GET /Books/GetQueueData`  
**Table:** `queue_monitor`

---

## Upstream database schema

Maintained by the Python service (not ASP.NET `ApplicationDbContext`).

### 1. `Temporary_database` — drafts

| Column | Type | Notes |
|--------|------|-------|
| `id` | INT AUTO_INCREMENT PK | |
| `user_id` | VARCHAR(255) | |
| `book_id` | VARCHAR(255) | |
| `chapter` | INT | |
| `chapter_name` | VARCHAR(255) | User-selected name after suggestions |
| `user_input` | TEXT | Original prompt |
| `content` | LONGTEXT | Generated HTML/text |
| `suggest_chapter_name` | TEXT | ~5 AI-suggested names |
| `highlight_of_previous_chapter` | LONGTEXT | Continuity for next chapter |
| `date` | DATE | DEFAULT CURRENT_DATE |
| `time` | TIME | DEFAULT CURRENT_TIME |

### 2. `User_confirm` — approved chapters

Same as temporary **without** `suggest_chapter_name`. Written when `POST /api/approve` succeeds with `"approve": true`.

### 3. `audio_transcriptions`

| Column | Type |
|--------|------|
| `id` | INT AUTO_INCREMENT PK |
| `date`, `time` | DATE, TIME |
| `user_input` | TEXT — transcribed text |
| `book_id` | VARCHAR(50) |
| `chapter` | INT |
| `user_id` | VARCHAR(50) |
| `audio_file_path` | VARCHAR(255) |

### 4. `queue_monitor`

| Column | Type |
|--------|------|
| `status_running` | INT |
| `status_waiting` | INT |
| `status_max_concurrent` | INT |
| `status_total_requests` | INT |
| `logs` | TEXT |
| `user_id`, `book_id`, `chapter` | VARCHAR / INT |
| `log_date`, `log_time` | DATE, TIME |

### 5. `error_logs`

| Column | Type |
|--------|------|
| `line_number` | INT |
| `error` | TEXT |
| `filename` | VARCHAR(255) |
| `error_date`, `error_time` | DATE, TIME |

---

## ASP.NET BFF route map

| Upstream | ASP.NET entry point |
|----------|---------------------|
| `POST /api/generate_chapter` | `POST /Books/AIGenerateBook` |
| `POST /api/edit` | `POST /Books/AIEditBook`, `POST /Books/EditChapter` |
| `POST /api/approve` | `POST /Books/FinalizeChapterAPI` |
| `POST /api/audio` | `POST /api/AudioToText/convert`, `POST /Audio/Upload` |
| `GET /api/queue-data` | `GET /Books/GetQueueData` |
| `POST /api/generate-cover` | `POST /Books/GenerateAICoverPreview`, `POST /Dashboard/GenerateCover` |
| `POST /api/generate-spine-book-cover` | `POST /Dashboard/GeneratePrintReadyCover` |
| `POST /api/edit-cover` | `POST /Books/EditAICoverPreview`, `POST /Dashboard/EditCover` |
| `POST /api/book_chapters_name` | `POST /Books/SuggestChapterNames` |
| `POST /api/refine_cover_prompt` | `POST /Books/RefineCoverPrompt` |
| `POST /api/suggest-cover-prompt-from-highlights` | `POST /Books/SuggestCoverPromptFromHighlights` |

**Publish / download BFF (no upstream call):**

| Route | Purpose |
|-------|---------|
| `GET /Dashboard/BookPageMetrics?bookId=` | Page count from manuscript + formatter |
| `GET /Dashboard/GetPrintReadyCoverAssets?bookId=` | Saved wrap assets + `pageCount` |
| `POST /Dashboard/SavePrintReadyComposedWrap` | Persist client-composed 300 DPI wrap |
| `POST /Dashboard/DownloadBookPdf` | Full print-ready PDF |
| `GET /api/kdp/calculate` | KDP dimension calculator (local) |

All outbound upstream calls attach **`X-API-Key`** via `IBookApiClient` / `ExternalApiKeyResolver`.

---

## Configuration & secrets

### `appsettings.json` — `ExternalApi` block

URLs are committed; **API key is never committed.**

```json
"ExternalApi": {
  "BaseUrl": "http://162.229.248.26:8001",
  "GenerateUrl": "http://162.229.248.26:8001/api/generate_chapter",
  "EditUrl": "http://162.229.248.26:8001/api/edit",
  "ApproveUrl": "http://162.229.248.26:8001/api/approve",
  "GenerateCoverUrl": "http://162.229.248.26:8001/api/generate-cover",
  "GenerateSpineBookCoverUrl": "http://162.229.248.26:8001/api/generate-spine-book-cover",
  "EditCoverUrl": "http://162.229.248.26:8001/api/edit-cover",
  "AudioUrl": "http://162.229.248.26:8001/api/audio",
  "QueueDataUrl": "http://162.229.248.26:8001/api/queue-data",
  "BookChaptersNameUrl": "http://162.229.248.26:8001/api/book_chapters_name",
  "ApiKey": ""
}
```

### Production (Ubuntu `/opt/EbookAI`)

```bash
# /etc/default/ebookai — mode 600, never commit
ExternalApi__ApiKey=YOUR_EXTERNAL_API_KEY
ConnectionStrings__DefaultConnection='Server=localhost;Port=3306;Database=ebookpublications;...'

set -a && source /etc/default/ebookai && set +a
export ASPNETCORE_ENVIRONMENT=Production
nohup dotnet publish/EBookDashboard.dll --urls http://0.0.0.0:5000 > nohup.out 2>&1 &
```

### Development

```bash
dotnet user-secrets set "ExternalApi:ApiKey" "YOUR_EXTERNAL_API_KEY" --project newEbook.csproj
```

---

## cURL cookbook

Replace `YOUR_KEY` with the value from `ExternalApi__ApiKey`.

```bash
# Queue status
curl -sS "http://162.229.248.26:8001/api/queue-data" \
  -H "X-API-Key: YOUR_KEY"

# Generate chapter
curl -sS -X POST "http://162.229.248.26:8001/api/generate_chapter" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_KEY" \
  -d '{"user_id":"u123","book_id":"b456","chapter":"18","user_input":"how gravity descover"}'

# Edit chapter
curl -sS -X POST "http://162.229.248.26:8001/api/edit" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_KEY" \
  -d '{"user_id":"u123","book_id":"b456","chapter":"18","changes":"Replace in heading 8790 with 6789"}'

# Approve chapter
curl -sS -X POST "http://162.229.248.26:8001/api/approve" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_KEY" \
  -d '{"user_id":"u123","book_id":"b456","chapter":"18","approve":true}'

# Print wrap — Peter Pan 40 pages (no spine text in export pipeline)
curl -sS -X POST "http://162.229.248.26:8001/api/generate-spine-book-cover" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_KEY" \
  -d @tests/payloads/generate_spine_book_cover.json
```

**REST Client:** use `smoke-tests.http` with `@BASE` and `@KEY`.

**Automated smoke:**

```bash
export API_BASE_URL=http://162.229.248.26:8001
export API_KEY=YOUR_KEY
python Scripts/smoke_test.py --json-output smoke-results.json
```

---

## Troubleshooting

| Symptom | Action |
|---------|--------|
| `401 Unauthorized` | Wrong/missing `X-API-Key`; rotate key if exposed |
| Spine too wide / narrow | Fix `page_count`; must match Book Formatting preview |
| Spine shows title text | Use latest `Clean_Code` + hard refresh; export strips spine text |
| SweetAlert shows wrong pages | Open Book Formatting first; saves `printReadyPageCount` |
| `ExternalApi:ApiKey is not set` | `source /etc/default/ebookai` before `nohup` |
| Upstream timeout on wrap | Long-running job; increase BFF long-timeout; check queue |
| Port 5000 in use | `kill -9 $(ss -tlnp \| grep 5000 \| grep -oP 'pid=\K[0-9]+')` |

---

## Related files

| File | Purpose |
|------|---------|
| `smoke-tests.http` | Manual REST Client tests |
| `tests/payloads/*.json` | Reusable JSON bodies |
| `docs/EXTERNAL_BOOK_API.md` | Shorter BFF-focused summary |
| `Scripts/smoke_test.py` | CI / pre-deploy smoke script |
| `API-LIVE-AUDIT.md` | Last live probe results |

---

*Maintained with `Controllers/BooksController.cs`, `Controllers/DashboardController.cs`, `Services/BookApi/*`, `wwwroot/js/cover-kdp-export.js`, and `Application/Kdp/`.*
