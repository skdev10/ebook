# EbookAI Book API — Complete Documentation

**Base URL:** `http://162.229.248.26:8001`

## Authentication

Every request needs:

```http
X-API-Key: YOUR_API_KEY
Content-Type: application/json
```

Set the key on the EbookAI server (recommended):

```bash
# /etc/default/ebookai or environment
ExternalApi__ApiKey=YOUR_API_KEY
```

Or local git-ignored `appsettings.Local.json`:

```json
{
  "ExternalApi": {
    "ApiKey": "YOUR_API_KEY",
    "BaseUrl": "http://162.229.248.26:8001"
  }
}
```

**Do not commit the live API key to git.** Rotate the key if it was shared in chat/docs.

---

## Quick reference

| Method | Endpoint | Purpose |
|--------|----------|---------|
| POST | `/api/generate_chapter` | Generate chapter text |
| POST | `/api/edit` | Edit a chapter |
| POST | `/api/audio` | Transcribe audio → text |
| POST | `/api/approve` | Confirm / approve chapter |
| GET | `/api/queue-data` | Running + waiting queue |
| POST | `/api/generate-cover` | Front cover only |
| POST | `/api/edit-cover` | Edit front cover (base64) |
| POST | `/api/book_chapters_name` | Suggest chapter names |
| POST | `/api/generate-spine-book-cover-split` | **Full wrap** (back + spine + front) |
| POST | `/api/generate-spine-book-cover` | **Full wrap** (AI one-shot) |

**VALID_SIZES:** `1024x1024`, `1536x1024`, `1024x1536`, `auto`  
**VALID_QUALITIES:** `low`, `medium`, `high`, `auto`  
**Audio:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

---

## Full print wrap (spine + back + front)

### A) Split wrap (recommended when you already have a front cover)

`POST /api/generate-spine-book-cover-split`

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

EbookAI may also send `encoded_image` (base64 of the saved front cover) so the wrap keeps your approved front art.

### B) Full spine wrap (AI draws back + spine + front together)

`POST /api/generate-spine-book-cover`

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

### EbookAI wrap pipeline (Cover Design → Publish)

1. **Local ImageSharp compositor** (seconds) — uses exact saved front  
2. If that fails → **`/api/generate-spine-book-cover-split`** (max ~2 min)  
3. If that fails → **`/api/generate-spine-book-cover`** (max ~2 min)  

Saved file key: `printReadyCoverWrap` — same file shown in Cover Design preview and downloaded on Publish.

---

## 1. Generate chapter

`POST /api/generate_chapter`

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "user_input": "how gravity descover"
}
```

Example result heading: `"The gravitational force is invented in 8790"`.

Stored in `Temporary_database` (includes `suggest_chapter_name` for ~5 name suggestions).

---

## 2. Edit chapter

`POST /api/edit`

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "replace in heading with 8790 with 6789"
}
```

---

## 3. Audio transcription

`POST /api/audio`

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 14,
  "audio_file_path": "c:\\book_project\\Audio_transcribe_into_text\\John s Morning Routi.mp3"
}
```

Supported: `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`  
Table: `audio_transcriptions`

---

## 4. Approve chapter

`POST /api/approve`

```json
{
  "user_id": "user_id",
  "book_id": "book_id",
  "chapter": 18,
  "approve": true
}
```

Moves data into `User_confirm`.

---

## 5. Queue status

`GET /api/queue-data`

Returns how many requests are **running** and how many are **waiting**.  
Logged in `queue_monitor`.

---

## 6. Generate front cover

`POST /api/generate-cover`

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

---

## 7. Edit cover

`POST /api/edit-cover`

```json
{
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA...",
  "image_direction": "image direction in prompt",
  "size": "1024x1536"
}
```

`encoded_image` = base64 of the image to edit.

---

## 8. Chapter name suggestions

`POST /api/book_chapters_name`

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "highlights": []
}
```

---

## Database tables

### 1. `Temporary_database` (draft)

| Column | Type | Notes |
|--------|------|--------|
| id | INT AUTO_INCREMENT PK | |
| user_id | VARCHAR(255) | |
| book_id | VARCHAR(255) | |
| chapter | INT | |
| chapter_name | VARCHAR(255) | |
| user_input | TEXT | |
| content | LONGTEXT | |
| suggest_chapter_name | TEXT | Suggests ~5 names; user picks → stored |
| highlight_of_previous_chapter | LONGTEXT | |
| date | DATE DEFAULT CURRENT_DATE | |
| time | TIME DEFAULT CURRENT_TIME | |

### 2. `User_confirm` (approved)

| Column | Type |
|--------|------|
| id | INT AUTO_INCREMENT PK |
| user_id | VARCHAR(255) |
| book_id | VARCHAR(255) |
| chapter | INT |
| chapter_name | VARCHAR(255) |
| user_input | TEXT |
| content | LONGTEXT |
| highlight_of_previous_chapter | LONGTEXT |
| date | DATE DEFAULT CURRENT_DATE |
| time | TIME DEFAULT CURRENT_TIME |

### 3. `audio_transcriptions`

| Column | Type |
|--------|------|
| id | INT AUTO_INCREMENT PK |
| date | DATE DEFAULT CURRENT_DATE |
| time | TIME DEFAULT CURRENT_TIME |
| user_input | TEXT |
| book_id | VARCHAR(50) |
| chapter | INT |
| user_id | VARCHAR(50) |
| audio_file_path | VARCHAR(255) |

### 4. `queue_monitor`

| Column | Type |
|--------|------|
| id | INT AUTO_INCREMENT PK |
| status_running | INT |
| status_waiting | INT |
| status_max_concurrent | INT |
| status_total_requests | INT |
| logs | TEXT |
| user_id | VARCHAR(50) |
| book_id | VARCHAR(50) |
| chapter | INT |
| log_date | DATE DEFAULT CURRENT_DATE |
| log_time | TIME DEFAULT CURRENT_TIME |

### 5. `error_logs`

| Column | Type |
|--------|------|
| id | INT AUTO_INCREMENT PK |
| line_number | INT |
| error | TEXT |
| filename | VARCHAR(255) |
| error_date | DATE DEFAULT CURRENT_DATE |
| error_time | TIME DEFAULT CURRENT_TIME |

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
```

---

## Common HTTP errors

| Code | Meaning |
|------|---------|
| 401 | Missing/invalid `X-API-Key` |
| 422 | Bad JSON / missing fields |
| 500 | Upstream error — check `error_logs` + `/api/queue-data` |
