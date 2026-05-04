# External Book, Audio & Cover API — Reference

**Technical documentation** for the REST service used by this ASP.NET dashboard.  
**Base URL:** `http://162.229.248.26:8001`

---

## Quick reference

| Endpoint | Method | Purpose |
|----------|--------|---------|
| `/api/generate_chapter` | POST | Generate chapter from `user_input` |
| `/api/edit` | POST | Edit chapter via natural-language `changes` |
| `/api/audio` | POST | Transcribe audio (server path) |
| `/api/approve` | POST | Confirm chapter (`approve: true` → `User_confirm`) |
| `/api/queue-data` | GET | Queue: running, waiting, limits |
| `/api/generate-cover` | POST | Generate cover image (size + quality enums) |
| `/api/edit-cover` | POST | Edit cover from Base64 + prompt |
| `/api/book_chapters_name` | POST | Save chapter names / highlights batch |

**Authentication (all endpoints above):** header `X-API-Key: <your key>`.

---

## Authentication

All listed endpoints require the following header on every request:

| Header | Value |
|--------|--------|
| `X-API-Key` | API key issued for the Python service. Configure in this app as `ExternalApi:ApiKey` (use **User Secrets** or environment variables in production—**do not commit keys to git**). |

**Example (curl):**

```bash
curl -s -X POST "http://162.229.248.26:8001/api/generate_chapter" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_API_KEY_HERE" \
  -d "{\"user_id\":\"u123\",\"book_id\":\"b456\",\"chapter\":\"18\",\"user_input\":\"how gravity was discovered\"}"
```

Raw HTTP:

```http
POST /api/generate_chapter HTTP/1.1
Host: 162.229.248.26:8001
Content-Type: application/json
X-API-Key: <YOUR_API_KEY>

{ "user_id": "u123", "book_id": "b456", "chapter": "1", "user_input": "..." }
```

**Errors:** Missing or invalid key typically yields **401** or **403**. Wrong JSON shape may yield **422 Unprocessable Entity**.

> **Security:** Treat the API key like a password. Rotate it if it was ever shared in chat, email, or a public repo. Never paste production keys into documentation or source control.

---

## Table of contents

1. [Generate Chapter](#1-generate-chapter--post-apigenerate_chapter)
2. [Edit Chapter](#2-edit-chapter--post-apiedit)
3. [Audio Transcription](#3-audio-transcription--post-apiaudio)
4. [Approve / Confirm Chapter](#4-approveconfirm-chapter--post-apiapprove)
5. [Queue Status](#5-queue-status--get-apiqueue-data)
6. [Generate Cover](#6-generate-cover--post-apigenerate-cover)
7. [Edit Cover](#7-edit-cover--post-apiedit-cover)
8. [Save Book Chapter Names](#8-save-book-chapter-names--post-apibook_chapters_name)
9. [Database schema (SQL reference)](#database-schema-sql-reference)
10. [Database context (reference)](#database-context-reference)
11. [ASP.NET integration: Base64 cover images](#aspnet-integration-base64-cover-images)

---

## 1. Generate Chapter — `POST /api/generate_chapter`

**Description:** Generates AI chapter body text from a user prompt. Persists work-in-progress data (see `Temporary_database`). Returns narrative content (often including a heading) and **five** suggested chapter titles for the UI.

| Item | Value |
|------|--------|
| **Method** | `POST` |
| **Full URL** | `http://162.229.248.26:8001/api/generate_chapter` |
| **Content-Type** | `application/json` |

### Request body schema

| Field | Type | Required | Notes |
|-------|------|----------|--------|
| `user_id` | string | Yes | Stable user identifier (e.g. `"u123"` or numeric id as string). |
| `book_id` | string | Yes | Book identifier. |
| `chapter` | string | Yes | Chapter index or label (e.g. `"18"`). |
| `user_input` | string | Yes | Natural-language topic or instructions (e.g. *"how gravity was discovered"*). |

### Request example

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "user_input": "how gravity was discovered"
}
```

### Response (illustrative)

Shape depends on the Python/FastAPI implementation. Typical fields:

| Field | Type | Notes |
|-------|------|--------|
| `status` | string | e.g. `"success"`. |
| `content` / `chapter_text` / `response` | string | Generated chapter HTML or plain text (exact key may vary). |
| `suggest_chapter_name` | string or array | **Five** AI-suggested chapter names; user picks one → stored via `/api/book_chapters_name` or your app DB (see **`Temporary_database.suggest_chapter_name`**). |
| `chapter` | string/number | Echo or normalized chapter index. |

Example: generated heading might read *“The gravitational force is invented in 8790”* inside `content`; use **`/api/edit`** with `changes` like *“replace in heading 8790 with 6789”* to fix it.

```json
{
  "status": "success",
  "chapter": "18",
  "content": "<h1>The gravitational force is invented in 8790</h1><p>Long-form narrative...</p>",
  "suggest_chapter_name": [
    "The Weight of the World",
    "How We Fell Toward Truth",
    "Newton's Shadow",
    "Orbits and Doubt",
    "The Pull of History"
  ]
}
```

---

## 2. Edit Chapter — `POST /api/edit`

**Description:** Applies **natural-language** edits to an existing chapter generated for the same `user_id` / `book_id` / `chapter`. The backend resolves the stored draft and applies `changes` (e.g. replace text in a heading).

Cover image pipelines use **`VALID_SIZES`** on **`/api/generate-cover`** and **`/api/edit-cover`** only:

`"1024x1024"`, `"1536x1024"`, `"1024x1536"`, `"auto"`

Chapter **edit** uses JSON below (no `size` field in the standard contract).

| Item | Value |
|------|--------|
| **Method** | `POST` |
| **Full URL** | `http://162.229.248.26:8001/api/edit` |
| **Content-Type** | `application/json` |

### Request body schema

| Field | Type | Required | Notes |
|-------|------|----------|--------|
| `user_id` | string | Yes | Must match the user who owns the draft. |
| `book_id` | string | Yes | Book id. |
| `chapter` | string | Yes | Same chapter key as generate. |
| `changes` | string | Yes | Human-readable edit instruction (e.g. *"replace in heading 8790 with 6789"*). |

### Request example

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "replace in heading 8790 with 6789"
}
```

### Response (illustrative)

```json
{
  "status": "success",
  "chapter": "18",
  "content": "<h1>The gravitational force is invented in 6789</h1><p>...</p>"
}
```

---

## 3. Audio Transcription — `POST /api/audio`

**Description:** Submits an audio file path (server-side path as seen by the API host) for transcription; results are associated with `user_id`, `book_id`, and `chapter`.

| Item | Value |
|------|--------|
| **Method** | `POST` |
| **Full URL** | `http://162.229.248.26:8001/api/audio` |
| **Content-Type** | `application/json` |

### Request body schema

| Field | Type | Required | Notes |
|-------|------|----------|--------|
| `user_id` | string | Yes | User id. |
| `book_id` | string | Yes | Book id. |
| `chapter` | number or string | Yes | Chapter number (example uses `14`). |
| `audio_file_path` | string | Yes | **Absolute path on the API server** (e.g. Windows `c:\\book_project\\audio\\file.mp3`). |

### Supported file extensions

`.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

### Request example

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 14,
  "audio_file_path": "c:\\book_project\\audio\\file.mp3"
}
```

### Response (illustrative)

```json
{
  "status": "success",
  "chapter": 14,
  "transcript": "Plain text or structured segments returned by the speech engine.",
  "language": "en"
}
```

> **Note:** Paths must be valid **on the machine running the API**, not necessarily the ASP.NET host. For browser uploads, a different flow (multipart upload to your app, then API) may be required.

---

## 4. Approve / Confirm Chapter — `POST /api/approve`

**Description:** Confirms a chapter: moves (or copies) data from **`Temporary_database`** to **`User_confirm`** when `approve` is `true`.

| Item | Value |
|------|--------|
| **Method** | `POST` |
| **Full URL** | `http://162.229.248.26:8001/api/approve` |
| **Content-Type** | `application/json` |

### Request body schema

| Field | Type | Required | Notes |
|-------|------|----------|--------|
| `user_id` | string | Yes | User identifier. |
| `book_id` | string | Yes | Book identifier. |
| `chapter` | number | Yes | Chapter number (integer). |
| `approve` | boolean | Yes | `true` to persist confirmation path. |

### Request example

JSON must use **double-quoted keys** and boolean **`true`** (lowercase). Do not use a key like `"chapter "` (trailing space).

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 18,
  "approve": true
}
```

### Response (illustrative)

```json
{
  "status": "success",
  "approved": true,
  "chapter": 18,
  "message": "Chapter moved to User_confirm"
}
```

---

## 5. Queue Status — `GET /api/queue-data`

**Description:** Returns operational queue metrics: running jobs, waiting jobs, concurrency limits, and totals (backed by **`queue_monitor`** / related logic).

| Item | Value |
|------|--------|
| **Method** | `GET` |
| **Full URL** | `http://162.229.248.26:8001/api/queue-data` |

**Request body:** none.

### Response (illustrative)

```json
{
  "running": 2,
  "waiting": 5,
  "max_concurrent": 4,
  "total_requests": 12840,
  "updated_at": "2026-03-27T12:00:00Z"
}
```

Field names may vary slightly; align with the live service or proxy (`Books/GetQueueData` in this app when configured).

---

## 6. Generate Cover — `POST /api/generate-cover`

**Description:** Generates a book cover image from metadata and style label. Response commonly includes **`encoded_image`** (raw Base64) and/or URL-like fields depending on deployment.

| Item | Value |
|------|--------|
| **Method** | `POST` |
| **Full URL** | `http://162.229.248.26:8001/api/generate-cover` |
| **Content-Type** | `application/json` |

### Request body schema

| Field | Type | Required | Notes |
|-------|------|----------|--------|
| `title` | string | Yes | Book title on cover. |
| `author_name` | string | Yes | Author name. |
| `category` | string | Yes | Genre / category (e.g. `"Science"`, `"Fantasy"`). |
| `cover_style` | string | Yes | Style label, e.g. `"Modern Illustration"` (map from UI slugs in ASP.NET). |
| `size` | string | Yes | See **valid sizes** below. |
| `quality` | string | Yes | See **valid qualities** below. |

### Valid `size` values (enum)

| Value | Meaning |
|-------|--------|
| `1024x1024` | Square 1024×1024 |
| `1536x1024` | Landscape |
| `1024x1536` | Portrait (common for book covers) |
| `auto` | Service picks dimensions |

### Valid `quality` values (enum)

**`VALID_QUALITIES`:** `"low"`, `"medium"`, `"high"`, `"auto"`.

| Value | Notes |
|-------|--------|
| `low` | Faster / smaller |
| `medium` | Balanced default |
| `high` | Higher fidelity |
| `auto` | Service default |

### Request example

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

### Response (illustrative)

```json
{
  "status": "success",
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==",
  "mime_type": "image/png"
}
```

Some deployments return `options` / `urls` arrays instead of or in addition to `encoded_image`. The ASP.NET client parses multiple shapes via `CoverExternalApiHelper.ExtractCoverImageUrlsFromApiResponse`.

---

## 7. Edit Cover — `POST /api/edit-cover`

**Description:** Refines an existing cover image using a Base64 source (typically from **`/api/generate-cover`**) plus natural-language **`image_direction`**.

| Item | Value |
|------|--------|
| **Method** | `POST` |
| **Full URL** | `http://162.229.248.26:8001/api/edit-cover` |
| **Content-Type** | `application/json` |

### Request body schema

| Field | Type | Required | Notes |
|-------|------|----------|--------|
| `encoded_image` | string | Yes | Raw Base64 **or** full `data:image/png;base64,...` string (confirm with live API). |
| `image_direction` | string | Yes | Edit instructions, e.g. *"make the background darker"*. |
| `size` | string | Yes | Same vocabulary as generate-cover (`1024x1536`, etc.). |

### Request example

```json
{
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA...",
  "image_direction": "make the background darker",
  "size": "1024x1536"
}
```

### Response (illustrative)

```json
{
  "status": "success",
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA..."
}
```

---

## 8. Save Book Chapter Names — `POST /api/book_chapters_name`

**Description:** Persists user-chosen chapter titles / highlights for a book (batch of `{ chapter, chapter_name }`).

| Item | Value |
|------|--------|
| **Method** | `POST` |
| **Full URL** | `http://162.229.248.26:8001/api/book_chapters_name` |
| **Content-Type** | `application/json` |

### Request body schema

| Field | Type | Required | Notes |
|-------|------|----------|--------|
| `user_id` | string | Yes | User id. |
| `book_id` | string | Yes | Book id. |
| `highlights` | array | Yes | List of highlight / naming objects. |

### `highlights[]` item schema

| Field | Type | Required | Notes |
|-------|------|----------|--------|
| `chapter` | number | Yes | Chapter index (1-based in example). |
| `chapter_name` | string | Yes | Display title. |

### Request example

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "highlights": [
    { "chapter": 1, "chapter_name": "Introduction" },
    { "chapter": 2, "chapter_name": "The Experiment" },
    { "chapter": 18, "chapter_name": "Gravity Revisited" }
  ]
}
```

### Response (illustrative)

```json
{
  "status": "success",
  "saved": 3
}
```

---

## Database schema (SQL reference)

Below is the **logical schema** as described for the Python API service (MySQL-style). Names and types should match the live database; adjust if migrations differ.

### 1. `Temporary_database` (draft / in-progress)

Stores temporary chapter data during generate/edit. Column **`suggest_chapter_name`** holds **five** suggested chapter names from the AI; the user selects or types a final name, which your flow then persists (e.g. via `/api/book_chapters_name` or app DB).

```sql
CREATE TABLE Temporary_database (
  id INT AUTO_INCREMENT PRIMARY KEY,
  user_id VARCHAR(255),
  book_id VARCHAR(255),
  chapter INT,
  chapter_name VARCHAR(255),
  user_input TEXT,
  content LONGTEXT,
  suggest_chapter_name TEXT,
  highlight_of_previous_chapter LONGTEXT,
  date DATE DEFAULT (CURRENT_DATE),
  time TIME DEFAULT (CURRENT_TIME)
);
```

### 2. `User_confirm` (user-approved chapters)

Populated when the user confirms a chapter (`POST /api/approve` with `approve: true`).

```sql
CREATE TABLE User_confirm (
  id INT AUTO_INCREMENT PRIMARY KEY,
  user_id VARCHAR(255),
  book_id VARCHAR(255),
  chapter INT,
  chapter_name VARCHAR(255),
  user_input TEXT,
  content LONGTEXT,
  highlight_of_previous_chapter LONGTEXT,
  date DATE DEFAULT (CURRENT_DATE),
  time TIME DEFAULT (CURRENT_TIME)
);
```

### 3. `audio_transcriptions` (audio)

```sql
CREATE TABLE audio_transcriptions (
  id INT AUTO_INCREMENT PRIMARY KEY,
  date DATE DEFAULT (CURRENT_DATE),
  time TIME DEFAULT (CURRENT_TIME),
  user_input TEXT,
  book_id VARCHAR(50),
  chapter INT,
  user_id VARCHAR(50),
  audio_file_path VARCHAR(255)
);
```

### 4. `queue_monitor`

```sql
CREATE TABLE queue_monitor (
  id INT AUTO_INCREMENT PRIMARY KEY,
  status_running INT,
  status_waiting INT,
  status_max_concurrent INT,
  status_total_requests INT,
  logs TEXT,
  user_id VARCHAR(50),
  book_id VARCHAR(50),
  chapter INT,
  log_date DATE DEFAULT (CURRENT_DATE),
  log_time TIME DEFAULT (CURRENT_TIME)
);
```

### 5. `error_logs`

```sql
CREATE TABLE error_logs (
  id INT AUTO_INCREMENT PRIMARY KEY,
  line_number INT,
  error TEXT,
  filename VARCHAR(255),
  error_date DATE DEFAULT (CURRENT_DATE),
  error_time TIME DEFAULT (CURRENT_TIME)
);
```

---

## Database context (reference)

| Table | Purpose |
|--------|--------|
| **`Temporary_database`** | In-progress chapters; includes **`suggest_chapter_name`** (five AI suggestions) until the user picks a final **`chapter_name`**. |
| **`User_confirm`** | Approved snapshots after **`POST /api/approve`** with **`approve: true`**. |
| **`audio_transcriptions`** | Audio job metadata (`audio_file_path`, linkage to user/book/chapter). |
| **`queue_monitor`** | Metrics aligned with **`GET /api/queue-data`** (running, waiting, max concurrent, totals, logs). |
| **`error_logs`** | Server-side error capture (file, line, message, time). |

**Flow summary:**

1. Generate / edit chapter → data in **`Temporary_database`**.  
2. Approve → row in **`User_confirm`**.  
3. Audio → **`audio_transcriptions`**.  
4. **`queue_monitor`** + **`error_logs`** → observability.

---

## ASP.NET (`appsettings.json`) mapping

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
  "ApiKey": "<from User Secrets in production>",
  "CoverGenerateSize": "1024x1536",
  "CoverGenerateQuality": "medium",
  "Sql_AlterSettingsValueLongText": "ALTER TABLE `Settings` MODIFY COLUMN `Value` LONGTEXT NULL;"
}
```

---

## ASP.NET integration: Base64 cover images

The service returns cover bytes as **Base64** (sometimes wrapped in a `data:image/...;base64,...` data URL).

This repository includes **`Base64CoverImageHelper`** in `Services/Base64CoverImageHelper.cs`:

- **`TryDecodeToImageBytes(string? input)`** — validates input, strips data-URL header if present, decodes Base64, sniffs PNG/JPEG/WebP/GIF magic bytes, returns **`DataUrl`** safe for `<img src="...">`.
- **`TrySaveToWebRoot(...)`** — optional: write decoded bytes under `wwwroot` and return a relative URL.

### Razor view (display only)

Pass a model string `EncodedImageFromApi` (raw Base64 or data URL), or use ViewBag:

```cshtml
@using EBookDashboard.Services
@{
    var decoded = Base64CoverImageHelper.TryDecodeToImageBytes(Model?.EncodedImageFromApi);
}
@if (decoded.Success)
{
    <img src="@decoded.DataUrl" alt="Generated cover" class="img-fluid" width="320" />
}
else
{
    <p class="text-danger">@decoded.Error</p>
}
```

### Controller action (save file + redirect to URL)

```csharp
var decoded = Base64CoverImageHelper.TryDecodeToImageBytes(encodedFromApi);
if (!decoded.Success) return BadRequest(decoded.Error);

var webRoot = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
var rel = Base64CoverImageHelper.TrySaveToWebRoot(decoded.Bytes!, webRoot, "uploads/covers", $"cover_{bookId}.png");
return Ok(new { url = rel });
```

Then in Razor: `<img src="@rel" alt="cover" />`.

See **`Services/Base64CoverImageHelper.cs`** for full XML comments and edge-case behavior.

---

## Troubleshooting

| Symptom | Likely cause |
|--------|----------------|
| **401 / 403** | Missing/wrong `X-API-Key`. |
| **422** | JSON keys/types don’t match FastAPI models. |
| **Cover OK in Postman, empty in browser** | Response JSON shape differs; extend `CoverExternalApiHelper.ExtractCoverImageUrlsFromApiResponse`. |
| **Timeouts** | Use `quality: "low"` or `size: "auto"` if supported; increase HttpClient timeout on ASP.NET side. |
| **Invalid Base64 in UI** | Use `Base64CoverImageHelper.TryDecodeToImageBytes` and log `Error` message. |

---

*Document version: April 2026. Align field names with the live OpenAPI/schema on the Python host when they diverge.*
