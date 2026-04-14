# AI Book Generation Platform — API Documentation

**Base URL:** `http://162.229.248.26:8001`  
**Authentication:** All requests must include the API key in the header.

---

## Authentication

| Header     | Value |
|-----------|--------|
| `X-API-Key` | `AK-proj-c8r15p0EYc1B0SKi5_hP58HEyL6xP0ywmZ2hEpvpvU5y-i7yZ8IiyLv1K7cGSkyNh` |

- Store the key in server configuration (e.g. `appsettings.json` → `ExternalApi:ApiKey`). Do not expose it in frontend code.
- Ensure no leading/trailing spaces in the key.

---

## 1. Generate Chapter

**Endpoint:** `POST http://162.229.248.26:8001/api/generate_chapter`

**Request:**
```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "user_input": "how gravity descover"
}
```

**Response example:** e.g. heading `"The gravitational force is invented in 8790"`.  
**Storage:** Save response in **Temporary_database**.

---

## 2. Edit Chapter

**Endpoint:** `POST http://162.229.248.26:8001/api/edit`

**Valid sizes (for reference):** `1024x1024`, `1536x1024`, `1024x1536`, `auto`

**Request:**
```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "change the title something else"
}
```

**Example:** To change 8790 to 6789 in the heading:  
`"changes": "replace in heading with 8790 with 6789"`

**Storage:** Updated content stays in temporary flow until user approves. Show Original | Updated side-by-side in UI.

---

## 3. Approve (Finalize) Chapter

**Endpoint:** `POST http://162.229.248.26:8001/api/approve`

**Request:**
```json
{
  "user_id": "user_id",
  "book_id": "book_id",
  "chapter": 18,
  "approve": true
}
```

**Storage:** On success, move data from **Temporary_database** to **User_confirm**.

---

## 4. Audio Transcription

**Endpoint:** `POST http://162.229.248.26:8001/api/audio`

**Request (form/multipart or JSON):**
- `user_id` — e.g. `"u123"`
- `book_id` — e.g. `"b456"`
- `chapter` — e.g. `14`
- `audio_file_path` — e.g. `r"c:\book_project\Audio_transcribe_into_text\John s Morning Routi.mp3"`

**Supported formats:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

**Storage:** **audio_transcriptions** table.

---

## 5. Queue Data

**Endpoint:** `GET http://162.229.248.26:8001/api/queue-data`

**Returns:** Number of requests running, waiting, max concurrent, total requests.

**Storage:** **queue_monitor** table.

---

## 6. Generate Cover

**Endpoint:** `POST http://162.229.248.26:8001/api/generate-cover`

**Valid sizes:** `1024x1024`, `1536x1024`, `1024x1536`, `auto`  
**Valid qualities:** `low`, `medium`, `high`, `auto`

**Request:**
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

Optional: some implementations accept `prompt` for extra instructions (e.g. `"prompt": "space theme, blue background"`).

**Response:** API may return `options` / `urls` (array of image URLs) or `url` / `image_url` / `cover_url` (single URL) or `image` (base64). This app normalizes these for the UI.

**Use only on:** Cover Design page (`/BookDesign/CoverDesignCalculatorFixing` or `/Dashboard/CoverDesign`), not on AI Generate Book page.

---

## 7. Edit Cover

**Endpoint:** `POST http://162.229.248.26:8001/api/edit-cover`

**Request:**
```json
{
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA...",
  "image_direction": "image direction in prompt",
  "size": "1024x1536"
}
```

- `encoded_image`: base64-encoded image to edit.  
- `image_direction`: text prompt for the edit (e.g. "change background color").  
- `size`: one of `1024x1024`, `1536x1024`, `1024x1536`, `auto`.

---

## 8. Chapter Name Suggestions

**Endpoint:** `POST http://162.229.248.26:8001/api/book_chapters_name`

**Request:**
```json
{
  "user_id": "str",
  "book_id": "str",
  "highlights": [ "HighlightItem" ]
}
```

**Response:** Suggested chapter names (e.g. 5 suggestions).  
**Storage:** Store in **Temporary_database.suggest_chapter_name**. User’s chosen name goes in **chapter_name**.

---

## Database Tables

Use these for storage. Do not add new columns/tables without approval.

### 1. Temporary_database (temporary data)

| Column                      | Type        | Notes |
|----------------------------|-------------|--------|
| id                         | INT         | AUTO_INCREMENT PRIMARY KEY |
| user_id                    | VARCHAR(255)| |
| book_id                    | VARCHAR(255)| |
| chapter                    | INT         | |
| chapter_name               | VARCHAR(255)| User-selected name |
| user_input                 | TEXT        | |
| content                    | LONGTEXT    | |
| suggest_chapter_name       | TEXT        | 5 suggested names from API |
| highlight_of_previous_chapter | LONGTEXT | |
| date                       | DATE        | DEFAULT (CURRENT_DATE) |
| time                       | TIME        | DEFAULT (CURRENT_TIME) |

### 2. User_confirm (approved data)

| Column                      | Type        | Notes |
|----------------------------|-------------|--------|
| id                         | INT         | AUTO_INCREMENT PRIMARY KEY |
| user_id                    | VARCHAR(255)| |
| book_id                    | VARCHAR(255)| |
| chapter                    | INT         | |
| chapter_name               | VARCHAR(255)| |
| user_input                 | TEXT        | |
| content                    | LONGTEXT    | |
| highlight_of_previous_chapter | LONGTEXT | |
| date                       | DATE        | DEFAULT (CURRENT_DATE) |
| time                       | TIME        | DEFAULT (CURRENT_TIME) |

### 3. audio_transcriptions

| Column          | Type         | Notes |
|-----------------|--------------|--------|
| id              | INT          | AUTO_INCREMENT PRIMARY KEY |
| date            | DATE         | DEFAULT (CURRENT_DATE) |
| time            | TIME         | DEFAULT (CURRENT_TIME) |
| user_input      | TEXT         | |
| book_id         | VARCHAR(50)  | |
| chapter         | INT          | |
| user_id         | VARCHAR(50)  | |
| audio_file_path | VARCHAR(255) | |

### 4. queue_monitor

| Column                  | Type         | Notes |
|-------------------------|--------------|--------|
| id                      | INT          | AUTO_INCREMENT PRIMARY KEY |
| status_running          | INT          | |
| status_waiting          | INT          | |
| status_max_concurrent   | INT          | |
| status_total_requests   | INT          | |
| logs                    | TEXT         | |
| user_id                 | VARCHAR(50)  | |
| book_id                 | VARCHAR(50)  | |
| chapter                 | INT          | |
| log_date                | DATE         | DEFAULT (CURRENT_DATE) |
| log_time                | TIME         | DEFAULT (CURRENT_TIME) |

### 5. error_logs

| Column      | Type         | Notes |
|-------------|--------------|--------|
| id          | INT          | AUTO_INCREMENT PRIMARY KEY |
| line_number | INT          | |
| error       | TEXT         | |
| filename    | VARCHAR(255) | |
| error_date  | DATE         | DEFAULT (CURRENT_DATE) |
| error_time  | TIME         | DEFAULT (CURRENT_TIME) |

---

## Backend Proxy Endpoints (This App)

These endpoints call the external API with `X-API-Key` from server config. Use them from the frontend.

| Purpose              | Method | URL (this app) | Notes |
|----------------------|--------|----------------|-------|
| Queue status         | GET    | `/Books/GetQueueData` | Proxies to GET /api/queue-data |
| Chapter name suggestions | POST | `/Books/BookChaptersName` | Body: `{ user_id, book_id, highlights }` |
| **Cover preview**    | POST   | `/Books/GenerateAICoverPreview?bookId={id}` | Body: `{ prompt, title, style }`. Sends title, author_name, category, cover_style, size, quality (and prompt if provided) to POST /api/generate-cover. |
| Finalize cover       | POST   | `/Books/FinalizeCover?bookId={id}` | Body: `{ coverPath }` — saves selected cover to book. |
| Audio transcription  | POST   | `/Audio/Upload` | Form: audio file, userId, bookId, chapter. Proxies to POST /api/audio |
| Edit chapter         | POST   | `/Books/AIEditBook` | Body: `{ UserId, BookId, Title, chapter, changes }`. Proxies to POST /api/edit |
| Approve chapter      | POST   | `/Books/FinalizeChapterAPI` | Body: `{ user_id, book_id, chapter, approve: true }`. Proxies to POST /api/approve |

---

## Error Handling

- Log all failures to **error_logs** (line_number, error, filename, error_date, error_time).
- Use timeouts and retry logic for external API calls.
- Return JSON from proxy endpoints (e.g. `{ success: false, message: "..." }`) so the frontend can show a clear error instead of "Generation failed."
