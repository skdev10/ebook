# External Book Service — API Reference

Upstream base URL: **`http://162.229.248.26:8001`**

This document describes the **upstream** REST API (Python/service).  
The ASP.NET app proxies most calls using `ExternalApi:*` URLs and **`ExternalApi:ApiKey`** — set the key via **environment** or **user secrets**, not in git.

---

## Security

| Item | Requirement |
|------|--------------|
| Header | **`X-API-Key`** on every request (unless your deployment uses a different header — this app sends `X-API-Key`) |
| Key storage | Use `ExternalApi__ApiKey` env var or `dotnet user-secrets set "ExternalApi:ApiKey" "<key>"` |
| Rotation | **Never paste production keys into chat or commit them.** If a key was exposed, rotate it on the upstream service immediately. |

**Example header**

```http
X-API-Key: YOUR_SECRET_KEY_HERE
```

---

## Quick reference

| # | Endpoint | Method | Purpose |
|---|----------|--------|---------|
| 1 | `/api/generate_chapter` | POST | Generate a chapter from `user_input` |
| 2 | `/api/edit` | POST | Edit an existing chapter (natural-language `changes`) |
| 3 | `/api/audio` | POST | Audio → text transcription |
| 4 | `/api/approve` | POST | User confirms chapter (move to confirmed storage on upstream DB) |
| 5 | `/api/queue-data` | GET | Queue: running / waiting / limits / totals |
| 6 | `/api/generate-cover` | POST | New cover image |
| 7 | `/api/edit-cover` | POST | Edit cover from base64 image + prompt |
| 8 | `/api/book_chapters_name` | POST | Suggest chapter names (`highlights` context) |

**Cover-only options (do *not* apply to `/api/edit` chapter prose):**

- **`VALID_SIZES`**: `1024x1024`, `1536x1024`, `1024x1536`, `auto`
- **`VALID_QUALITIES`** (generate-cover): `low`, `medium`, `high`, `auto`

---

## 1. Generate chapter

**`POST /api/generate_chapter`**

**Body (JSON)** — identifiers are strings unless your worker accepts numbers:

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "user_input": "how gravity descover"
}
```

**Note:** The .NET proxy (`POST /Books/AIGenerateBook`) also sends `title`, `chapter` as **int**, `user_input`, and `preview_only` per `AIBookRequest` — aligned with upstream expectations.

**Example outcome:** Response may include a heading like *"The gravitational force is invented in 8790"* (upstream content shape varies).

**Typical persistence (upstream):** `Temporary_database`.

---

## 2. Edit chapter

**`POST /api/edit`**

Edits prose via **`changes`** (plain-language instructions).

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "Replace in heading 8790 with 6789"
}
```

Examples for `changes`:

- `"Change the title to something about Newton"`
- `"Replace in heading with 8790 with 6789"`

Chapter edit **does not** use image `VALID_SIZES`; those apply only to **cover** endpoints.

**.NET proxy:** `POST /Books/AIEditBook` — sends `user_id`, `book_id`, `title`, `chapter`, `changes` (see controller).

---

## 3. Audio transcription

**`POST /api/audio`**

**Supported file types:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

### Option A — JSON with server-local path (upstream / same machine only)

Illustrative (Python-style). Real JSON requires proper quoting:

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 14,
  "audio_file_path": "c:\\book_project\\Audio_transcribe_into_text\\JohnsMorningRoutine.mp3"
}
```

### Option B — Multipart upload (recommended for browsers / this app)

This app uploads the file bytes with form fields:

- `user_id`, `book_id`, `chapter`
- Optional: `audio_file_path` when `ExternalApi:AudioSendLocalFilePath` is true (advanced)
- File field names tried: `audio`, `audio_file`, `file` (see `ExternalApi:AudioMultipartFieldNames`)

**.NET proxies**

- `POST /Audio/Upload` (multipart)
- `POST /api/AudioToText/convert` (`AudioToTextController`, multipart)

---

## 4. Approve (confirm) chapter

**`POST /api/approve`**

Use valid JSON (**no trailing spaces in keys**, booleans lowercase in JSON):

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "approve": true
}
```

`chapter` may be sent as a string or number depending on upstream; this app serializes **`APIFinalizeChapterRequest`** with snake_case keys.

**.NET proxy:** `POST /Books/FinalizeChapterAPI`

**Typical persistence (upstream):** `User_confirm`

---

## 5. Queue status

**`GET /api/queue-data`**

No body. Returns queue metrics (e.g. running count, waiting, max concurrent, total requests) — exact JSON keys depend on upstream implementation.

**.NET proxy:** `GET /Books/GetQueueData`

**Typical persistence (upstream):** `queue_monitor`

---

## 6. Generate cover

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

- **`size`**: `1024x1024` \| `1536x1024` \| `1024x1536` \| `auto`
- **`quality`**: `low` \| `medium` \| `high` \| `auto`

**.NET:** `DashboardController` / `BooksController` cover preview actions use `GenerateCoverUrl` + `CoverGenerateSize` / `CoverGenerateQuality` defaults from config.

---

## 7. Edit cover

**`POST /api/edit-cover`**

```json
{
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA...",
  "image_direction": "Warm sunset palette, subtle sci-fi typography",
  "size": "1024x1536"
}
```

- **`encoded_image`**: raw base64 (no `data:image/png;base64,` prefix unless upstream documents it).
- **`image_direction`**: edit instructions (prompt).

**.NET:** e.g. `DashboardController` edit-cover endpoint forwarding to `ExternalApi:EditCoverUrl`.

---

## 8. Chapter name suggestions

**`POST /api/book_chapters_name`**

```json
{
  "user_id": "42",
  "book_id": "59",
  "highlights": [
    { "chapter": 1, "summary": "Hook: protagonist discovers anomaly." },
    { "chapter": 2, "summary": "Rising stakes in the laboratory." }
  ]
}
```

`highlights` is a list of context objects — align field names with your upstream **`HighlightItem`** schema.

**.NET proxy:** `POST /Books/BookChaptersName` (forwards JSON body).

**Persistence:** upstream may populate **`Temporary_database.suggest_chapter_name`** (e.g. five suggestions); user’s chosen title can be saved in **`chapter_name`**.

---

## Upstream database tables (reference)

Schemas are maintained by the upstream service. Typical usage:

### 1. `Temporary_database`

Temporary drafts: `user_id`, `book_id`, `chapter`, `chapter_name`, `user_input`, `content`, `suggest_chapter_name`, `highlight_of_previous_chapter`, timestamps.  
**Note:** `suggest_chapter_name` may store suggested names before the user picks one.

### 2. `User_confirm`

Approved chapters after **`/api/approve`**.

### 3. `audio_transcriptions`

Audio workflow: paths or transcription metadata (`user_input`, `book_id`, `chapter`, `user_id`, `audio_file_path`, …).

### 4. `queue_monitor`

Queue metrics history (`status_running`, `status_waiting`, `status_max_concurrent`, `status_total_requests`, `logs`, …).

### 5. `error_logs`

Upstream error logging (`line_number`, `error`, `filename`, …).

---

## ASP.NET proxies (this repository)

Configured under **`ExternalApi`** in `appsettings.json` (URLs) and **`ExternalApi:ApiKey`** (secret from env/secrets).

| Upstream | This app entry point |
|----------|----------------------|
| `POST /api/generate_chapter` | `POST /Books/AIGenerateBook` |
| `POST /api/edit` | `POST /Books/AIEditBook`, `Books/EditChapter`, etc. |
| `POST /api/approve` | `POST /Books/FinalizeChapterAPI` |
| `GET /api/queue-data` | `GET /Books/GetQueueData` |
| `POST /api/book_chapters_name` | `POST /Books/BookChaptersName` |
| `POST /api/generate-cover` | Cover flows in `BooksController` / `DashboardController` |
| `POST /api/edit-cover` | Dashboard / Books edit-cover handlers |
| `POST /api/audio` | `POST /Audio/Upload`, `POST /api/AudioToText/convert` |

All integrated paths send **`X-API-Key`** when `ExternalApi:ApiKey` is set.

---

## cURL templates

Replace `YOUR_KEY` and base URL if needed.

```bash
# Generate chapter
curl -sS -X POST "http://162.229.248.26:8001/api/generate_chapter" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_KEY" \
  -d '{"user_id":"u123","book_id":"b456","chapter":"18","user_input":"topic here"}'

# Edit chapter
curl -sS -X POST "http://162.229.248.26:8001/api/edit" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_KEY" \
  -d '{"user_id":"u123","book_id":"b456","chapter":"18","changes":"Replace 8790 with 6789 in the heading"}'

# Queue
curl -sS "http://162.229.248.26:8001/api/queue-data" \
  -H "X-API-Key: YOUR_KEY"

# Approve
curl -sS -X POST "http://162.229.248.26:8001/api/approve" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_KEY" \
  -d '{"user_id":"u123","book_id":"b456","chapter":"18","approve":true}'
```

---

## Local configuration snippet

```json
"ExternalApi": {
  "BaseUrl": "http://162.229.248.26:8001",
  "GenerateUrl": "http://162.229.248.26:8001/api/generate_chapter",
  "EditUrl": "http://162.229.248.26:8001/api/edit",
  "ApproveUrl": "http://162.229.248.26:8001/api/approve",
  "GenerateCoverUrl": "http://162.229.248.26:8001/api/generate-cover",
  "EditCoverUrl": "http://162.229.248.26:8001/api/edit-cover",
  "AudioUrl": "http://162.229.248.26:8001/api/audio",
  "QueueDataUrl": "http://162.229.248.26:8001/api/queue-data",
  "BookChaptersNameUrl": "http://162.229.248.26:8001/api/book_chapters_name",
  "ApiKey": ""
}
```

Set the key at deploy time:

```bash
export ExternalApi__ApiKey="your-key-here"
```

Windows (PowerShell): `$env:ExternalApi__ApiKey = "your-key-here"`

---

*Document version: aligns with codebase `Controllers/BooksController.cs`, `Services/ExternalBookApiAudio.cs`, and `appsettings.json` ExternalApi keys.*
