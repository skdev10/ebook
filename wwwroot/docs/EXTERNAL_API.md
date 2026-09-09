# EbookAI Book API — Complete Documentation

**Base URL:** `http://162.229.248.26:8001`

---

## Authentication

Every request must include:

```http
X-API-Key: YOUR_API_KEY
Content-Type: application/json
```

Header name: **`X-API-Key`**

### Configure the key on EbookAI (ASP.NET)

**Recommended (Linux / Production)** — environment variable:

```bash
# /etc/default/ebookai
ExternalApi__ApiKey=YOUR_API_KEY
ExternalApi__BaseUrl=http://162.229.248.26:8001
```

**Local development** — git-ignored `appsettings.Local.json`:

```json
{
  "ExternalApi": {
    "ApiKey": "YOUR_API_KEY",
    "BaseUrl": "http://162.229.248.26:8001"
  }
}
```

**Do not commit the live API key to git.** If a key was pasted in chat, email, or docs, rotate it.

---

## Quick reference

| Method | Endpoint | Purpose |
|--------|----------|---------|
| POST | `/api/generate_chapter` | Generate chapter text from user input |
| POST | `/api/edit` | Edit a particular chapter |
| POST | `/api/audio` | Transcribe audio → chapter text |
| POST | `/api/approve` | Confirm / approve chapter |
| GET | `/api/queue-data` | Running + waiting queue status |
| POST | `/api/generate-cover` | Front cover only |
| POST | `/api/edit-cover` | Edit front cover (base64 image) |
| POST | `/api/book_chapters_name` | Suggest chapter names from highlights |
| POST | `/api/generate-spine-book-cover-split` | Full wrap: spine + back + front |
| POST | `/api/generate-spine-book-cover` | Full wrap: one-shot AI wrap |

**VALID_SIZES:** `1024x1024`, `1536x1024`, `1024x1536`, `auto`  
**VALID_QUALITIES:** `low`, `medium`, `high`, `auto`  
**Audio formats:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

---

## Full print wrap (spine + back + front)

These endpoints design the **full paperback wrap**: back cover | spine | front cover.

### A) Split wrap — `/api/generate-spine-book-cover-split`

Use when you already have (or will send) a front cover. EbookAI sends optional `encoded_image` (base64 of the saved front) so the wrap keeps your approved front art.

`POST http://162.229.248.26:8001/api/generate-spine-book-cover-split`

```json
{
  "title": "The Iqbal Day",
  "author_name": "Sara Khan",
  "size": "1536x1024",
  "quality": "high",
  "Interior_trim_size": "6 x 9 in",
  "paper_type": "white",
  "page_count": 40
}
```

Optional (sent by EbookAI when composing from a saved front):

```json
{
  "encoded_image": "<base64 of front cover PNG/JPG>"
}
```

### B) Full spine wrap — `/api/generate-spine-book-cover`

AI draws back + spine + front together in one image.

`POST http://162.229.248.26:8001/api/generate-spine-book-cover`

```json
{
  "title": "Peter Pan",
  "author_name": "J. M. Barrie",
  "size": "1536x1024",
  "quality": "high",
  "Interior_trim_size": "6 x 9 in",
  "page_count": 40,
  "paper_type": "white"
}
```

### How EbookAI uses these (Cover Design → Publish)

1. **Local ImageSharp compositor** (seconds) — places your exact saved front on the front panel; builds back + spine locally  
2. Background fallback (if local fails): **`/api/generate-spine-book-cover-split`** (~2 min max)  
3. Then: **`/api/generate-spine-book-cover`** (~2 min max)  

Saved Settings key: `printReadyCoverWrap` — same file for Cover Design preview and Publish download.

---

## Chapters

### 1. Generate chapter — `/api/generate_chapter`

From user input, generate chapter content.

`POST http://162.229.248.26:8001/api/generate_chapter`

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "user_input": "how gravity descover"
}
```

**Example result:** heading such as `"The gravitational force is invented in 8790"`.

Stored in **`Temporary_database`** (includes `suggest_chapter_name` for ~5 name suggestions).

---

### 2. Edit chapter — `/api/edit`

Edit a particular chapter (title/body) using a natural-language change instruction.

`POST http://162.229.248.26:8001/api/edit`

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "replace in heading with 8790 with 6789"
}
```

**Example:** change year `8790` → `6789` by describing that replacement in `changes`.

---

### 3. Audio transcription — `/api/audio`

`POST http://162.229.248.26:8001/api/audio`

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 14,
  "audio_file_path": "c:\\book_project\\Audio_transcribe_into_text\\John s Morning Routi.mp3"
}
```

**Supported formats:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`  
Table: **`audio_transcriptions`**

---

### 4. Approve chapter — `/api/approve`

Confirm the chapter (moves draft → confirmed).

`POST http://162.229.248.26:8001/api/approve`

```json
{
  "user_id": "user_id",
  "book_id": "book_id",
  "chapter": 18,
  "approve": true
}
```

Moves data into **`User_confirm`**.

---

### 5. Queue status — `/api/queue-data`

`GET http://162.229.248.26:8001/api/queue-data`

Returns how many requests are **running** and how many people are **waiting**.  
Logged in **`queue_monitor`**.

---

## Covers (front only)

### 6. Generate front cover — `/api/generate-cover`

`POST http://162.229.248.26:8001/api/generate-cover`

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

Sizes: `VALID_SIZES` · Qualities: `VALID_QUALITIES`

---

### 7. Edit cover — `/api/edit-cover`

`POST http://162.229.248.26:8001/api/edit-cover`

```json
{
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA...",
  "image_direction": "image direction in prompt",
  "size": "1024x1536"
}
```

`encoded_image` = base64 of the image to edit.

---

### 8. Chapter name suggestions — `/api/book_chapters_name`

`POST http://162.229.248.26:8001/api/book_chapters_name`

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "highlights": [
    {
      "chapter_name": "Artificial Intelligence",
      "detailed_bullet_summary": "Book title: Artificial Intelligence. Suggest five chapter titles about this book."
    }
  ]
}
```

`highlights` is a list of `{ chapter_name, detailed_bullet_summary }`. The Writer wand icon sends the current book title here. The API returns about five names (`suggest_chapter_name` / `chapter_titles`); the user picks one and it is stored.

---

## Database tables

### 1. `Temporary_database` (draft / temporary)

Used to save temporary chapter data.

| Column | Type | Notes |
|--------|------|--------|
| id | INT AUTO_INCREMENT PRIMARY KEY | |
| user_id | VARCHAR(255) | |
| book_id | VARCHAR(255) | |
| chapter | INT | |
| chapter_name | VARCHAR(255) | |
| user_input | TEXT | |
| content | LONGTEXT | |
| suggest_chapter_name | TEXT | Suggests ~5 chapter names; user picks → stored |
| highlight_of_previous_chapter | LONGTEXT | |
| date | DATE DEFAULT (CURRENT_DATE) | |
| time | TIME DEFAULT (CURRENT_TIME) | |

**Note:** `suggest_chapter_name` suggests about 5 chapter names; after user input, the chosen name is stored in the DB.

### 2. `User_confirm` (approved)

Confirm data saved when the user approves.

| Column | Type |
|--------|------|
| id | INT AUTO_INCREMENT PRIMARY KEY |
| user_id | VARCHAR(255) |
| book_id | VARCHAR(255) |
| chapter | INT |
| chapter_name | VARCHAR(255) |
| user_input | TEXT |
| content | LONGTEXT |
| highlight_of_previous_chapter | LONGTEXT |
| date | DATE DEFAULT (CURRENT_DATE) |
| time | TIME DEFAULT (CURRENT_TIME) |

### 3. `audio_transcriptions`

| Column | Type |
|--------|------|
| id | INT AUTO_INCREMENT PRIMARY KEY |
| date | DATE DEFAULT (CURRENT_DATE) |
| time | TIME DEFAULT (CURRENT_TIME) |
| user_input | TEXT |
| book_id | VARCHAR(50) |
| chapter | INT |
| user_id | VARCHAR(50) |
| audio_file_path | VARCHAR(255) |

### 4. `queue_monitor`

| Column | Type |
|--------|------|
| id | INT AUTO_INCREMENT PRIMARY KEY |
| status_running | INT |
| status_waiting | INT |
| status_max_concurrent | INT |
| status_total_requests | INT |
| logs | TEXT |
| user_id | VARCHAR(50) |
| book_id | VARCHAR(50) |
| chapter | INT |
| log_date | DATE DEFAULT (CURRENT_DATE) |
| log_time | TIME DEFAULT (CURRENT_TIME) |

### 5. `error_logs`

| Column | Type |
|--------|------|
| id | INT AUTO_INCREMENT PRIMARY KEY |
| line_number | INT |
| error | TEXT |
| filename | VARCHAR(255) |
| error_date | DATE DEFAULT (CURRENT_DATE) |
| error_time | TIME DEFAULT (CURRENT_TIME) |

---

## cURL examples

```bash
export API_KEY='YOUR_API_KEY'
export BASE='http://162.229.248.26:8001'

# Queue
curl -s -H "X-API-Key: $API_KEY" "$BASE/api/queue-data"

# Full wrap (split)
curl -s -X POST "$BASE/api/generate-spine-book-cover-split" \
  -H "X-API-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '{"title":"The Iqbal Day","author_name":"Sara Khan","size":"1536x1024","quality":"high","Interior_trim_size":"6 x 9 in","paper_type":"white","page_count":40}'

# Full wrap (one-shot)
curl -s -X POST "$BASE/api/generate-spine-book-cover" \
  -H "X-API-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '{"title":"Peter Pan","author_name":"J. M. Barrie","size":"1536x1024","quality":"high","Interior_trim_size":"6 x 9 in","page_count":40,"paper_type":"white"}'

# Front cover
curl -s -X POST "$BASE/api/generate-cover" \
  -H "X-API-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '{"title":"The Power of Gravity","author_name":"Hasan Rahim","category":"Science","cover_style":"Modern Illustration","size":"1024x1536","quality":"medium"}'

# Generate chapter
curl -s -X POST "$BASE/api/generate_chapter" \
  -H "X-API-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '{"user_id":"u123","book_id":"b456","chapter":"18","user_input":"how gravity descover"}'
```

---

## Common HTTP errors

| Code | Meaning |
|------|---------|
| 401 | Missing or invalid `X-API-Key` |
| 422 | Bad JSON / missing fields |
| 500 | Upstream error — check `error_logs` and `/api/queue-data` |

---

## Related EbookAI app settings

| Setting | Purpose |
|---------|---------|
| `ExternalApi:BaseUrl` | Book API host |
| `ExternalApi:ApiKey` | Value for `X-API-Key` (set via `ExternalApi__ApiKey` env — **never commit the live key**) |
| `ExternalApi:GenerateUrl` | `/api/generate_chapter` |
| `ExternalApi:EditUrl` | `/api/edit` |
| `ExternalApi:ApproveUrl` | `/api/approve` |
| `ExternalApi:AudioUrl` | `/api/audio` |
| `ExternalApi:GenerateCoverUrl` | `/api/generate-cover` |
| `ExternalApi:EditCoverUrl` | `/api/edit-cover` |
| `ExternalApi:GenerateSpineBookCoverSplitUrl` | Full wrap split |
| `ExternalApi:GenerateSpineBookCoverUrl` | Full wrap one-shot |
| `ExternalApi:QueueDataUrl` | `/api/queue-data` |
| `ExternalApi:BookChaptersNameUrl` | `/api/book_chapters_name` |

---

## How EbookAI (this ASP.NET app) calls each API

| Upstream | Called from | App route / service |
|----------|-------------|---------------------|
| `POST /api/generate_chapter` | AI Writer → Generate | `POST /Books/AIGenerateBook` → `GenerateChapterPayloadBuilder` (`user_id`/`book_id`/`chapter` as **strings**) |
| `POST /api/edit` | AI Writer → Edit | `POST /Books/AIEditBook` |
| `POST /api/approve` | Finalize chapter | `POST /Books/FinalizeChapterAPI` |
| `POST /api/audio` | Voice / audio upload in Writer | `POST /api/AudioToText/convert` |
| `GET /api/queue-data` | Health + status | `GET /Books/GetQueueData`, `/health` |
| `POST /api/generate-cover` | Cover Design | `POST /Dashboard/GenerateCover` / `CoverFrontGenerationService` |
| `POST /api/edit-cover` | Cover Design edit | `POST /Dashboard/EditCover` |
| `POST /api/generate-spine-book-cover-split` | Full wrap fallback | `PrintWrapGenerationService` (after local ImageSharp) |
| `POST /api/generate-spine-book-cover` | Full wrap last resort | `PrintWrapGenerationService` |
| `POST /api/book_chapters_name` | Chapter name suggestions | `POST /Books/BookChaptersName` |

**JSON catalog (machine-readable):** `GET /Books/ApiDocumentation`  
**This markdown:** `/docs/EXTERNAL_API.md` (also under repo `docs/EXTERNAL_API.md`)

### Important payload rules

- `generate_chapter` / `edit`: `user_id`, `book_id`, and `chapter` must be **strings** (ints → HTTP 422).
- Chapter generate can take **several minutes**; check `/api/queue-data` if it hangs.
- Live mic needs **HTTPS** (or localhost). On plain HTTP, use the **audio upload** icon — text still fills the topic textarea via `/api/audio`.

