# External AI API Documentation

This document describes the external AI book-generation service used by the eBook platform.
The service handles chapter generation, editing, audio transcription, cover generation, and queue monitoring.

---

## Base URL

```
http://162.229.248.26:8001
```

---

## Authentication

All requests must include an API key in the request header.

| Header       | Value                                                                 |
|--------------|-----------------------------------------------------------------------|
| `X-API-Key`  | `AK-proj-c8r15p0EYc1B0SKi5_hP58HEyL6xP0ywmZ2hEpvpvU5y-i7yZ8IiyLv1K7cGSkyNh` |

> **Security note:** Treat this key as a secret. Do not commit it to source control or expose it in client-side code. On the server it is supplied via the environment variable `ExternalApi__ApiKey`.

Example header:

```
X-API-Key: AK-proj-c8r15p0EYc1B0SKi5_hP58HEyL6xP0ywmZ2hEpvpvU5y-i7yZ8IiyLv1K7cGSkyNh
Content-Type: application/json
```

---

## Common Field Reference

| Field        | Type    | Description                                  |
|--------------|---------|----------------------------------------------|
| `user_id`    | string  | Unique identifier of the user.               |
| `book_id`    | string  | Unique identifier of the book.               |
| `chapter`    | int/str | Chapter number.                              |
| `user_input` | string  | The user's prompt/topic for generation.      |

### Allowed image sizes

```
VALID_SIZES = ["1024x1024", "1536x1024", "1024x1536", "auto"]
```

### Allowed image qualities

```
VALID_QUALITIES = ["low", "medium", "high", "auto"]
```

### Supported audio formats

```
.mp3  .mp4  .mpeg  .mpga  .m4a  .wav  .webm
```

---

## Endpoints

### 1. Generate Chapter

Generates a new chapter from a user topic/prompt.

- **Method:** `POST`
- **URL:** `/api/generate_chapter`
- **Full URL:** `http://162.229.248.26:8001/api/generate_chapter`

**Request body:**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "user_input": "how gravity discovered"
}
```

**Example result:**

The generated chapter is returned with a heading, e.g.:

```
heading: "The gravitational force is invented in 8790"
```

---

### 2. Edit Chapter

Edits an existing chapter based on requested changes.

- **Method:** `POST`
- **URL:** `/api/edit`
- **Full URL:** `http://162.229.248.26:8001/api/edit`

**Request body:**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "change the title something else"
}
```

**Example:**

If the generated heading contained `8790` and you want it to be `6789`:

```
changes: "replace in heading 8790 with 6789"
```

---

### 3. Approve / Confirm Chapter

Confirms a chapter so its data is moved into the permanent `User_confirm` table.

- **Method:** `POST`
- **URL:** `/api/approve`
- **Full URL:** `http://162.229.248.26:8001/api/approve`

**Request body:**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 18,
  "approve": true
}
```

---

### 4. Audio Transcription

Transcribes an audio file into text for a given chapter.

- **Method:** `POST`
- **URL:** `/api/audio`
- **Full URL:** `http://162.229.248.26:8001/api/audio`

**Request body:**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 14,
  "audio_file_path": "c:\\book_project\\Audio_transcribe_into_text\\John s Morning Routine.mp3"
}
```

**Supported audio formats:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

---

### 5. Queue Data

Returns how many requests are currently running and how many are waiting in the queue.

- **Method:** `GET`
- **URL:** `/api/queue-data`
- **Full URL:** `http://162.229.248.26:8001/api/queue-data`

**Response (conceptual):** current `running`, `waiting`, `max_concurrent`, and `total_requests` counters (see the `queue_monitor` table below).

---

### 6. Generate Cover

Generates a book cover image.

- **Method:** `POST`
- **URL:** `/api/generate-cover`
- **Full URL:** `http://162.229.248.26:8001/api/generate-cover`

**Request body:**

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

- `size` must be one of `VALID_SIZES`.
- `quality` must be one of `VALID_QUALITIES`.

---

### 7. Edit Cover

Edits an existing cover image (provided as a base64-encoded image) using a text direction.

- **Method:** `POST`
- **URL:** `/api/edit-cover`
- **Full URL:** `http://162.229.248.26:8001/api/edit-cover`

**Request body:**

```json
{
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA...",
  "image_direction": "image direction in prompt",
  "size": "1024x1536"
}
```

- `encoded_image`: the base64-encoded image you want to edit.
- `image_direction`: the prompt describing how to modify the image.
- `size` must be one of `VALID_SIZES`.

---

### 8. Book Chapters Name

Suggests / sets chapter names based on highlights.

- **Method:** `POST`
- **URL:** `/api/book_chapters_name`
- **Full URL:** `http://162.229.248.26:8001/api/book_chapters_name`

**Request body:**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "highlights": [
    { "highlight": "..." }
  ]
}
```

- `highlights` is a list of `HighlightItem` objects.

---

## Database Schema

The service uses the following tables.

### 1. `Temporary_database`

Stores temporary (unconfirmed) generated data.

```sql
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
```

> **Note:** The column `suggest_chapter_name` suggests 5 chapter names. The user then provides input, and the chosen name is stored in the database.

---

### 2. `User_confirm`

Stores confirmed data after the user approves a chapter.

```sql
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
```

---

### 3. `audio_transcriptions`

Stores audio transcription records.

```sql
id INT AUTO_INCREMENT PRIMARY KEY,
date DATE DEFAULT (CURRENT_DATE),
time TIME DEFAULT (CURRENT_TIME),
user_input TEXT,
book_id VARCHAR(50),
chapter INT,
user_id VARCHAR(50),
audio_file_path VARCHAR(255)
```

---

### 4. `queue_monitor`

Tracks queue/concurrency state.

```sql
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
```

---

### 5. `error_logs`

Stores error records.

```sql
id INT AUTO_INCREMENT PRIMARY KEY,
line_number INT,
error TEXT,
filename VARCHAR(255),
error_date DATE DEFAULT (CURRENT_DATE),
error_time TIME DEFAULT (CURRENT_TIME)
```

---

## Typical Flow

1. **Generate** a chapter → `POST /api/generate_chapter` (data saved to `Temporary_database`).
2. **Edit** the chapter if needed → `POST /api/edit`.
3. **Suggest/Set** chapter names → `POST /api/book_chapters_name`.
4. **Approve** the chapter → `POST /api/approve` (data moved to `User_confirm`).
5. **Generate/Edit cover** → `POST /api/generate-cover` / `POST /api/edit-cover`.
6. **Audio transcription** (optional) → `POST /api/audio`.
7. **Monitor queue** anytime → `GET /api/queue-data`.

---

## cURL Examples

**Generate chapter:**

```bash
curl -X POST http://162.229.248.26:8001/api/generate_chapter \
  -H "X-API-Key: AK-proj-c8r15p0EYc1B0SKi5_hP58HEyL6xP0ywmZ2hEpvpvU5y-i7yZ8IiyLv1K7cGSkyNh" \
  -H "Content-Type: application/json" \
  -d '{"user_id":"u123","book_id":"b456","chapter":"18","user_input":"how gravity discovered"}'
```

**Check queue:**

```bash
curl -X GET http://162.229.248.26:8001/api/queue-data \
  -H "X-API-Key: AK-proj-c8r15p0EYc1B0SKi5_hP58HEyL6xP0ywmZ2hEpvpvU5y-i7yZ8IiyLv1K7cGSkyNh"
```
