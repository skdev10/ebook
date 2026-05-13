# Book platform API reference (upstream + ASP.NET BFF)

This document covers:

1. **Upstream book service** (Python / FastAPI style) at base URL **`http://162.229.248.26:8001`**
2. **This ASP.NET app** as a BFF (Backend for Frontend): same **`X-API-Key`**, JSON contracts, longer timeouts where configured

**Important — API keys**

- Authentication header: **`X-API-Key`**
- Configure with environment variable **`ExternalApi__ApiKey`** (or `OpenAI__ApiKey` as fallback resolver), **user secrets**, or **`appsettings.Local.json`** (gitignored).
- **Never commit real keys to git.** If a key was pasted in chat, email, or a ticket, **rotate it on the upstream server** and set the new value only in secure configuration.

---

## Quick reference (upstream)

| # | Path | Method | Purpose |
|---|------|--------|---------|
| 1 | `/api/generate_chapter` | POST | Generate chapter from `user_input` |
| 2 | `/api/edit` | POST | Edit chapter via natural-language `changes` |
| 3 | `/api/audio` | POST | Transcribe audio (multipart file or optional `audio_file_path`) |
| 4 | `/api/approve` | POST | Confirm chapter → upstream **`User_confirm`** |
| 5 | `/api/queue-data` | GET | Queue: running / waiting / max concurrent / totals |
| 6 | `/api/generate-cover` | POST | Generate cover options |
| 7 | `/api/edit-cover` | POST | Edit cover from base64 + prompt |
| 8 | `/api/book_chapters_name` | POST | Suggest chapter names from `highlights` |
| 9 | `/api/refine_cover_prompt` | POST | Refine a user’s cover prompt text |
| 10 | `/api/suggest-cover-prompt-from-highlights` | POST | Suggest cover prompt from book highlights |

**Cover-only constants** (not used by `/api/edit` chapter text):

- **`VALID_SIZES`**: `1024x1024`, `1536x1024`, `1024x1536`, `auto`
- **`VALID_QUALITIES`** (`generate-cover`): `low`, `medium`, `high`, `auto`

---

## 1. Generate chapter

**`POST /api/generate_chapter`**

**Headers:** `Content-Type: application/json`, **`X-API-Key: <secret>`**

**Example body (upstream-friendly):**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "user_input": "how gravity descover"
}
```

`chapter` may be a **string** or **number** depending on worker; both are common.

**ASP.NET BFF:** `POST /Books/AIGenerateBook` (JSON body from `AIGenerateBook` page).

The server forwards a payload built from **`AIBookRequest`**: it includes `user_id`, `book_id`, `chapter` (integer in app model), `user_input`, `title`, **`preview_only`** (dashboard uses `true` for drafts), and appends an internal **author / generation rules** suffix to `user_input` via `GenerateChapterPayloadBuilder` before calling upstream.

**Typical upstream table:** `Temporary_database`

---

## 2. Edit chapter

**`POST /api/edit`**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "In the heading, replace 8790 with 6789"
}
```

**ASP.NET BFF:** `POST /Books/AIEditBook` (JSON: `user_id`, `book_id`, `title`, `chapter`, `changes`).  
Also: `POST /Books/EditChapter` (JSON `APIEditChapterRequest`), legacy **`POST /Books/EditChapterFromQuery`** for query-style forwards.

**Note:** `VALID_SIZES` / `VALID_QUALITIES` apply only to **cover** endpoints, not chapter edit.

---

## 3. Audio transcription

**`POST /api/audio`**

**Supported extensions:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

### A) JSON + server-local file path (same machine as API only)

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 14,
  "audio_file_path": "c:\\book_project\\Audio_transcribe_into_text\\John s Morning Routi.mp3"
}
```

### B) Multipart (recommended for browsers and this app)

Form fields:

- `user_id`, `book_id`, `chapter`
- File part: try field names in order **`audio`**, **`audio_file`**, **`file`** (override with `ExternalApi:AudioMultipartFieldNames`)
- Optional: `audio_file_path` when `ExternalApi:AudioSendLocalFilePath` is `true`

**ASP.NET BFF:**

- `POST /api/AudioToText/convert` — multipart from browser; server may call upstream with multipart or Whisper fallback
- `POST /Audio/Upload` — MVC upload path

**Typical upstream table:** `audio_transcriptions`

---

## 4. Approve (confirm) chapter

**`POST /api/approve`**

Use strict JSON (no stray spaces in property names):

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "approve": true
}
```

Invalid examples to avoid: `"chapter "` (trailing space), Python-style `approve: True` without JSON quoting.

**ASP.NET BFF:** `POST /Books/FinalizeChapterAPI` — body maps to **`APIFinalizeChapterRequest`** (`user_id`, `book_id`, `chapter`, `approve`).

**Typical upstream table:** `User_confirm`

---

## 5. Queue status

**`GET /api/queue-data`**

No body. Returns metrics (shape depends on upstream), e.g. running / waiting / max concurrent / total.

**ASP.NET BFF:** `GET /Books/GetQueueData`

**Typical upstream table:** `queue_monitor`

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

**ASP.NET:** `BooksController` / `DashboardController` cover actions; defaults from `ExternalApi:CoverGenerateSize` and `ExternalApi:CoverGenerateQuality`.

---

## 7. Edit cover

**`POST /api/edit-cover`**

```json
{
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA...",
  "image_direction": "image direction in prompt",
  "size": "1024x1536"
}
```

- `encoded_image`: raw base64 unless upstream documents a `data:` prefix
- `image_direction`: edit instructions

---

## 8. Chapter name suggestions

**`POST /api/book_chapters_name`**

The worker’s `HighlightItem` schema may vary. Examples:

**Shape A (numeric chapter + summary):**

```json
{
  "user_id": "42",
  "book_id": "59",
  "highlights": [
    { "chapter": 1, "summary": "Hook: protagonist discovers anomaly." }
  ]
}
```

**Shape B (chapter_name + detailed_bullet_summary):**

```json
{
  "user_id": "u1",
  "book_id": "b1",
  "highlights": [
    {
      "chapter_name": "Chapter 1",
      "detailed_bullet_summary": "..."
    }
  ]
}
```

**ASP.NET BFF:** `POST /Books/BookChaptersName` — forwards JSON **as received** to upstream.

**Upstream note:** `Temporary_database.suggest_chapter_name` may hold ~5 suggestions; user-selected name can go into `chapter_name`.

---

## 9. Refine cover prompt

**`POST /api/refine_cover_prompt`**

Use **`http://`** unless TLS is correctly configured on the host (avoid mixed `https://` on an HTTP-only port).

```json
{
  "user_prompt": "here is the prompt"
}
```

**ASP.NET BFF:** `POST /Books/RefineCoverPrompt` — requires signed-in user session. URL from `ExternalApi:RefineCoverPromptUrl` or default path above.

---

## 10. Suggest cover prompt from highlights

**`POST /api/suggest-cover-prompt-from-highlights`**

```json
{
  "user_id": "u1",
  "book_id": "b1",
  "highlights": [
    {
      "chapter_name": "Chapter 1",
      "detailed_bullet_summary": "..."
    }
  ]
}
```

**ASP.NET BFF:** `POST /Books/SuggestCoverPromptFromHighlights` — requires session.

---

## Upstream database tables (reference)

Maintained by the upstream service; typical columns:

### 1. `Temporary_database`

Draft rows: `user_id`, `book_id`, `chapter`, `chapter_name`, `user_input`, `content`, **`suggest_chapter_name`**, `highlight_of_previous_chapter`, `date`, `time`, etc.

### 2. `User_confirm`

Confirmed chapters after **`/api/approve`**.

### 3. `audio_transcriptions`

Audio / transcription metadata (`user_input`, `book_id`, `chapter`, `user_id`, `audio_file_path`, timestamps).

### 4. `queue_monitor`

`status_running`, `status_waiting`, `status_max_concurrent`, `status_total_requests`, `logs`, optional `user_id` / `book_id` / `chapter`, timestamps.

### 5. `error_logs`

`line_number`, `error`, `filename`, timestamps.

---

## ASP.NET → upstream mapping (BFF)

All outbound calls that use **`IBookApiClient`** add **`X-API-Key`** from `ExternalApiKeyResolver` (same key source as `ExternalApi:ApiKey` / `OpenAI:ApiKey` fallback).

| Upstream | ASP.NET entry point (examples) |
|----------|----------------------------------|
| `POST /api/generate_chapter` | `POST /Books/AIGenerateBook` |
| `POST /api/edit` | `POST /Books/AIEditBook`, `POST /Books/EditChapter`, `POST /Books/EditChapterFromQuery` |
| `POST /api/approve` | `POST /Books/FinalizeChapterAPI` |
| `GET /api/queue-data` | `GET /Books/GetQueueData` |
| `POST /api/book_chapters_name` | `POST /Books/BookChaptersName` |
| `POST /api/generate-cover` | Books / Dashboard cover generate actions |
| `POST /api/edit-cover` | Books / Dashboard edit-cover actions |
| `POST /api/audio` | `POST /api/AudioToText/convert`, `POST /Audio/Upload` |
| `POST /api/refine_cover_prompt` | `POST /Books/RefineCoverPrompt` |
| `POST /api/suggest-cover-prompt-from-highlights` | `POST /Books/SuggestCoverPromptFromHighlights` |

**Pipeline:** `BookApiShort` (standard) vs **`BookApiLong`** (chapter generate). Resilience timeouts are configured in `Infrastructure/BookUpstreamHttpClientExtensions.cs`. Browser wait: `ChapterGeneration:BrowserFetchTimeoutMinutes`. IIS: see **`web.config`** `requestTimeout` when hosting in-process.

---

## cURL (upstream direct)

Replace `YOUR_KEY` and URLs if your deployment differs.

```bash
curl -sS -X POST "http://162.229.248.26:8001/api/generate_chapter" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_KEY" \
  -d "{\"user_id\":\"u123\",\"book_id\":\"b456\",\"chapter\":\"18\",\"user_input\":\"how gravity descover\"}"

curl -sS -X POST "http://162.229.248.26:8001/api/edit" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_KEY" \
  -d "{\"user_id\":\"u123\",\"book_id\":\"b456\",\"chapter\":\"18\",\"changes\":\"Replace in heading 8790 with 6789\"}"

curl -sS "http://162.229.248.26:8001/api/queue-data" \
  -H "X-API-Key: YOUR_KEY"

curl -sS -X POST "http://162.229.248.26:8001/api/approve" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: YOUR_KEY" \
  -d "{\"user_id\":\"u123\",\"book_id\":\"b456\",\"chapter\":\"18\",\"approve\":true}"
```

---

## `appsettings.json` — `ExternalApi` block

URLs are committed; **API key is not**. Example:

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
  "RefineCoverPromptUrl": "http://162.229.248.26:8001/api/refine_cover_prompt",
  "SuggestCoverPromptFromHighlightsUrl": "http://162.229.248.26:8001/api/suggest-cover-prompt-from-highlights",
  "ApiKey": ""
}
```

**Set key (Linux/macOS):** `export ExternalApi__ApiKey='your-key'`  
**Windows PowerShell:** `$env:ExternalApi__ApiKey = 'your-key'`

**Development:** `dotnet user-secrets set "ExternalApi:ApiKey" "your-key" --project newEbook.csproj`

---

## Troubleshooting

| Symptom | Check |
|---------|--------|
| “ExternalApi:ApiKey is not set” | Env var / user secrets / `appsettings.Local.json` |
| Generation stops after N minutes | `ChapterGeneration:BrowserFetchTimeoutMinutes`, reverse proxy timeouts, `web.config` |
| 401/403 from upstream | Wrong or expired `X-API-Key`; rotate key |
| Empty or HTML error from BFF | Upstream down or URL typo; check app logs for `BookApi` lines |
| Refine / suggest cover 404 | `RefineCoverPromptUrl` / `SuggestCoverPromptFromHighlightsUrl` must be absolute `http(s)://...` paths |

---

*Aligned with `Controllers/BooksController.cs`, `Services/ExternalBookApiAudio.cs`, `Services/BookApi/*`, `Infrastructure/BookUpstreamHttpClientExtensions.cs`, and `appsettings.json`.*
