# External book AI service — API reference

This document describes the **upstream HTTP API** used for chapter generation, editing, audio transcription, approval, queue status, cover art, and chapter-name suggestions. The EBookDashboard app calls these endpoints using values under the `ExternalApi` section in `appsettings.json` (see [Configuration](#configuration)).

**Security:** The repo ships a default `ExternalApi:ApiKey` in `appsettings.json` for local/dev parity with your upstream service. For production, prefer **environment variables** (`ExternalApi__ApiKey`) or `appsettings.Local.json` (gitignored — copy from `appsettings.Local.json.example`) so the key is not in source control long term. Rotate the key if it was ever exposed in chat or logs.

---

## Authentication

| Header       | Value        |
|-------------|--------------|
| `X-API-Key` | Your API key |

All requests below require this header unless the upstream service is explicitly open (not recommended).

---

## Base URL

Default in this project: `http://162.229.248.26:8001`

Individual endpoint URLs can be overridden per key in `ExternalApi` (for example if you move the service behind HTTPS or another host).

---

## 1. Generate chapter

**POST** `/api/generate_chapter`

**JSON body**

| Field          | Type   | Description |
|----------------|--------|-------------|
| `user_id`      | string | User identifier (e.g. `"u123"` or numeric id as string). |
| `book_id`      | string | Book identifier. |
| `chapter`      | number | Chapter index (this app sends an **int**; upstream may also accept string). |
| `title`        | string | Chapter / context title (optional in some flows; this app sends it). |
| `user_input`   | string | Prompt and continuity instructions. |
| `preview_only` | bool   | When `true`, upstream may skip persisting to its DB (dashboard preview flow). |

**Example**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 18,
  "title": "Chapter 18",
  "user_input": "how gravity discovered",
  "preview_only": false
}
```

**EBookDashboard:** `POST /Books/AIGenerateBook` (JSON body: `AIBookRequest`) and chapter pipeline via `BookChapterPipelineService` (same external contract; payload may append internal prose rules).

---

## 2. Edit chapter

**POST** `/api/edit`

**JSON body**

| Field      | Type   | Description |
|------------|--------|-------------|
| `user_id`  | string | User id. |
| `book_id`  | string | Book id. |
| `chapter`  | string | Chapter number as string (e.g. `"18"`) is accepted by upstream examples. |
| `changes`  | string | Natural-language edit instructions (e.g. replace title text). |

**Example**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "In the heading, replace 8790 with 6789"
}
```

**Cover / image note:** Valid image sizes for cover-related endpoints are: `1024x1024`, `1536x1024`, `1024x1536`, `auto`.

**EBookDashboard:** `POST /Books/AIEditBook` (JSON: `AIBookRequestEdit`) and `POST /Books/EditChapter` (query/form parameters forwarded as JSON).

---

## 3. Audio → text

**POST** `/api/audio`

**Multipart form** (typical)

| Part / field        | Description |
|---------------------|-------------|
| `user_id`           | String user id. |
| `book_id`           | String book id. |
| `chapter`           | Chapter number. |
| `audio_file_path`   | Server-side path string (when the worker runs on the same machine as storage). |
| `audio_file`        | File upload: binary audio. |

**Supported formats:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

**EBookDashboard:** `POST /Audio/Upload` saves the file under `wwwroot/uploads/audio`, then posts multipart to the external URL with `user_id`, `book_id`, `chapter`, `audio_file_path`, and `audio_file`.

---

## 4. Approve (confirm) chapter

**POST** `/api/approve`

**JSON body** (use exact property names; no trailing spaces in keys.)

| Field      | Type | Description |
|------------|------|-------------|
| `user_id`  | string | User id. |
| `book_id`  | string | Book id. |
| `chapter`  | string | Chapter number as string. |
| `approve`  | bool | `true` to confirm. |

**Example**

```json
{
  "user_id": "42",
  "book_id": "7",
  "chapter": "14",
  "approve": true
}
```

**EBookDashboard:** `POST /Books/FinalizeChapterAPI` with body matching `APIFinalizeChapterRequest`.

---

## 5. Queue status

**GET** `/api/queue-data`

Returns JSON describing running jobs, waiting queue, max concurrency, totals, etc. (aligned with upstream `queue_monitor` usage).

**EBookDashboard:** `GET /Books/GetQueueData` (proxies with `X-API-Key`).

---

## 6. Generate cover

**POST** `/api/generate-cover`

**JSON body**

| Field          | Type   | Description |
|----------------|--------|-------------|
| `title`        | string | Book title. |
| `author_name`  | string | Author display name. |
| `category`     | string | e.g. Science, Fiction. |
| `cover_style`  | string | Style label (this app maps UI style keys to labels). |
| `size`         | string | One of: `1024x1024`, `1536x1024`, `1024x1536`, `auto`. |
| `quality`      | string | One of: `low`, `medium`, `high`, `auto`. |

Defaults in config: `ExternalApi:CoverGenerateSize`, `ExternalApi:CoverGenerateQuality`.

**EBookDashboard:** `POST /Dashboard/GenerateCover` and related book cover actions in `BooksController` (see code paths using `GenerateCoverUrl`).

---

## 7. Edit cover

**POST** `/api/edit-cover`

**JSON body**

| Field               | Type   | Description |
|---------------------|--------|-------------|
| `encoded_image`     | string | Base64 PNG/JPEG (data URL prefix may be stripped in app code before send). |
| `image_direction`   | string | Prompt describing the desired change. |
| `size`              | string | Same valid sizes as generate cover. |

**EBookDashboard:** `POST /Dashboard/EditCover` (and book cover edit endpoints using `EditCoverUrl`).

---

## 8. Suggested chapter names

**POST** `/api/book_chapters_name`

**JSON body** (upstream contract)

| Field        | Type   | Description |
|--------------|--------|-------------|
| `user_id`    | string | User id. |
| `book_id`    | string | Book id. |
| `highlights` | array  | List of highlight objects (`HighlightItem`) for context. |

Downstream storage may use `suggest_chapter_name` on the temporary side (see database overview below).

**EBookDashboard:** `POST /Books/BookChaptersName` (forwards JSON body as-is).

---

## Configuration

In `appsettings.json` (or environment-specific files), set:

| Key | Purpose |
|-----|---------|
| `ExternalApi:ApiKey` | **Required** for calls that add `X-API-Key`. |
| `ExternalApi:GenerateUrl` | Generate chapter URL. |
| `ExternalApi:EditUrl` | Edit chapter URL. |
| `ExternalApi:ApproveUrl` | Approve chapter URL. |
| `ExternalApi:AudioUrl` | Audio transcription URL. |
| `ExternalApi:QueueDataUrl` | Queue GET URL. |
| `ExternalApi:GenerateCoverUrl` | Generate cover URL. |
| `ExternalApi:EditCoverUrl` | Edit cover URL. |
| `ExternalApi:BookChaptersNameUrl` | Chapter names suggestion URL. |
| `ChapterGeneration:HttpTimeoutMinutes` | HTTP client timeout for long chapter generation (see `Program.cs`). |

**User Secrets (development)**

```bash
dotnet user-secrets set "ExternalApi:ApiKey" "<paste-your-key-here>"
```

**Environment variable (deployment)**

`ExternalApi__ApiKey`

---

## Upstream database tables (reference)

These tables live on the **external** AI service’s database, not necessarily in EBookDashboard’s MySQL schema. They document how the remote service organizes work.

1. **`Temporary_database`** — Draft chapter rows: `user_id`, `book_id`, `chapter`, `chapter_name`, `user_input`, `content`, `suggest_chapter_name`, `highlight_of_previous_chapter`, date/time, etc.

2. **`User_confirm`** — Confirmed chapters after user approval.

3. **`audio_transcriptions`** — Audio path, transcription text, `user_id`, `book_id`, `chapter`, etc.

4. **`queue_monitor`** — Running/waiting counts, max concurrency, totals, logs, optional `user_id` / `book_id` / `chapter` on log rows.

5. **`error_logs`** — Line number, error text, filename, timestamp.

---

## Quick map: this app → external route

| Dashboard / app route | External API |
|----------------------|--------------|
| `POST /Books/AIGenerateBook` | `POST .../api/generate_chapter` |
| `POST /Books/AIEditBook`, `POST /Books/EditChapter` | `POST .../api/edit` |
| `POST /Books/FinalizeChapterAPI` | `POST .../api/approve` |
| `POST /Audio/Upload` | `POST .../api/audio` |
| `GET /Books/GetQueueData` | `GET .../api/queue-data` |
| `POST /Books/BookChaptersName` | `POST .../api/book_chapters_name` |
| `POST /Dashboard/GenerateCover` | `POST .../api/generate-cover` |
| `POST /Dashboard/EditCover` | `POST .../api/edit-cover` |

All of the above send **`X-API-Key`** when `ExternalApi:ApiKey` is configured.
