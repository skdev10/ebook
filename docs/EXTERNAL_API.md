# EbookAI Book API — Documentation

**Base URL:** `http://162.229.248.26:8001`

**Authentication (every request):**

```http
X-API-Key: <your-api-key>
Content-Type: application/json
```

Configure the key on the EbookAI server as `ExternalApi__ApiKey` (e.g. `/etc/default/ebookai` or git-ignored `appsettings.Local.json`). **Do not commit the live key to git.**

---

## Quick reference

| Method | Endpoint | Purpose |
|--------|----------|---------|
| POST | `/api/generate_chapter` | Generate chapter text from user input |
| POST | `/api/edit` | Edit a particular chapter |
| POST | `/api/audio` | Transcribe audio → chapter text |
| POST | `/api/approve` | Confirm / approve a chapter |
| GET | `/api/queue-data` | Running + waiting queue counts |
| POST | `/api/generate-cover` | Generate front cover image |
| POST | `/api/edit-cover` | Edit cover from base64 image |
| POST | `/api/book_chapters_name` | Suggest chapter names from highlights |

**Cover sizes (`VALID_SIZES`):** `1024x1024`, `1536x1024`, `1024x1536`, `auto`  
**Cover quality (`VALID_QUALITIES`):** `low`, `medium`, `high`, `auto`  
**Audio formats:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

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

**Example result:** heading like `"The gravitational force is invented in 8790"`.

Temporary rows are stored in `Temporary_database` (including `suggest_chapter_name` for up to 5 name suggestions).

---

## 2. Edit chapter

`POST /api/edit`

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "change the title something else"
}
```

**Example:** replace `8790` with `6789` in the heading:

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

Supported extensions: `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`.  
Rows are stored in `audio_transcriptions`.

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

Moves confirmed content into `User_confirm`.

---

## 5. Queue status

`GET /api/queue-data`

Returns how many requests are **running** and how many are **waiting**.  
Mirrored / logged in `queue_monitor`.

---

## 6. Generate cover (front)

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

EbookAI Cover Design calls this for the **front panel only**. For Paperback/Both, the website then builds the **full KDP wrap locally** from that saved front (back + spine + front) so preview matches export.

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

`encoded_image` = base64 of the image to edit (no `data:` prefix required by some clients; EbookAI normalizes both forms).

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

`highlights` is a list of `HighlightItem` objects from prior chapters. Suggestions can be stored in `Temporary_database.suggest_chapter_name`.

---

## Database tables

### 1. `Temporary_database` (draft / in-progress)

| Column | Type | Notes |
|--------|------|--------|
| id | INT AUTO_INCREMENT PK | |
| user_id | VARCHAR(255) | |
| book_id | VARCHAR(255) | |
| chapter | INT | |
| chapter_name | VARCHAR(255) | |
| user_input | TEXT | |
| content | LONGTEXT | |
| suggest_chapter_name | TEXT | Suggests ~5 chapter names; user picks → name stored |
| highlight_of_previous_chapter | LONGTEXT | |
| date | DATE DEFAULT CURRENT_DATE | |
| time | TIME DEFAULT CURRENT_TIME | |

### 2. `User_confirm` (approved chapters)

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

## EbookAI website mapping

| Screen | Uses |
|--------|------|
| AI Writer — generate chapter | `POST /api/generate_chapter` |
| AI Writer — edit chapter | `POST /api/edit` |
| AI Writer — audio | `POST /api/audio` |
| AI Writer — approve | `POST /api/approve` |
| Cover Design — Generate Cover | `POST /api/generate-cover` |
| Cover Design — Edit cover | `POST /api/edit-cover` |
| Cover Design — Full wrap (Paperback/Both) | **Local ImageSharp compositor** from saved front (not the long AI wrap APIs) |
| Publish — download wrap | Saved `printReadyCoverWrap` file (same as Cover Design preview) |
| Dashboard queue probe | `GET /api/queue-data` |

### Cover Design → Publish gate

1. Generate **front** cover (`/api/generate-cover`).
2. Website saves front to Settings (`printReadyCoverFront`).
3. For **Paperback / Both**, website composes full wrap locally (seconds).
4. **Continue to Publish** stays disabled until that wrap file is ready and visible.
5. Publish **Download full wrap cover** downloads that same file.

---

## cURL examples

```bash
# Queue
curl -s -H "X-API-Key: $API_KEY" \
  http://162.229.248.26:8001/api/queue-data

# Generate chapter
curl -s -X POST http://162.229.248.26:8001/api/generate_chapter \
  -H "X-API-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '{"user_id":"u123","book_id":"b456","chapter":"18","user_input":"how gravity descover"}'

# Generate cover
curl -s -X POST http://162.229.248.26:8001/api/generate-cover \
  -H "X-API-Key: $API_KEY" -H "Content-Type: application/json" \
  -d '{"title":"The Power of Gravity","author_name":"Hasan Rahim","category":"Science","cover_style":"Modern Illustration","size":"1024x1536","quality":"medium"}'
```

---

## Common errors

| HTTP | Meaning |
|------|---------|
| 401 | Missing/invalid `X-API-Key` |
| 422 | Invalid / incomplete JSON body |
| 500 | Upstream error — check `error_logs` and `/api/queue-data` |
