# Book API — Complete Reference

This guide documents every external Book API endpoint used by EbookAI, plus how the website maps each screen to those calls.

**Base URL:** `http://162.229.248.26:8001`

**Authentication (all endpoints):**

```http
X-API-Key: YOUR_API_KEY_HERE
Content-Type: application/json
```

Store the key in `/etc/default/ebookai` as `ExternalApi__ApiKey=...` on the server, or in git-ignored `appsettings.Local.json` locally. **Never commit the live key to git.**

---

## Two servers

| What | Address | Who uses it |
|------|---------|-------------|
| **Website (EbookAI)** | Your deployed site (e.g. `http://138.197.76.70:5000`) | Browser — login required |
| **Book API (AI backend)** | `http://162.229.248.26:8001` | Website background jobs + direct integrations |

---

## Quick reference

| # | Endpoint | Method | Purpose | Typical speed |
|---|----------|--------|---------|---------------|
| 1 | `/api/generate_chapter` | POST | AI writes a chapter | Slow (queue) |
| 2 | `/api/edit` | POST | Edit chapter text | Slow (queue) |
| 3 | `/api/audio` | POST | Transcribe audio → text | Medium |
| 4 | `/api/approve` | POST | Confirm chapter (draft → final) | Fast |
| 5 | `/api/queue-data` | GET | Queue status | Fast |
| 6 | `/api/generate-cover` | POST | Front cover image | Slow (queue) |
| 7 | `/api/edit-cover` | POST | Edit cover from base64 | Slow (queue) |
| 8 | `/api/book_chapters_name` | POST | Suggest next chapter names | Fast |
| 9 | `/api/refine_cover_prompt` | POST | Improve cover prompt | Fast |
| 10 | `/api/suggest-cover-prompt-from-highlights` | POST | Cover prompt from summaries | Fast |
| 11 | `/api/generate-spine-book-cover` | POST | Full wrap (AI generates front+spine+back together) | Slow |
| 12 | `/api/generate-spine-book-cover-split` | POST | **Full wrap from saved front cover** (recommended) | Slow (~1–2 min) |

**Print wrap in EbookAI (fallback chain):**

1. **`/api/generate-spine-book-cover-split`** — keeps your exact front (only on some API builds)
2. **`/api/generate-spine-book-cover`** — documented full AI wrap; the wrap's front panel is then saved as the book's front cover so preview and wrap always match
3. **Local compositor** — offline fallback built from the saved front image

Whichever succeeds first wins. The front cover shown in Cover Design always matches the wrap's front panel.

**Valid cover sizes:** `1024x1024`, `1536x1024`, `1024x1536`, `auto`

**Valid cover quality:** `low`, `medium`, `high`, `auto`

**Valid audio formats:** `.mp3`, `.mp4`, `.mpeg`, `.mpga`, `.m4a`, `.wav`, `.webm`

---

## Cover Design workflow (EbookAI)

| Step | User action | Backend |
|------|-------------|---------|
| 1 | Write **Image Direction** → click **Generate Cover** | `POST /api/generate-cover` (front only) |
| 2 | Wait for **Full print wrap** (Paperback/Both only) | Local compositor from saved front (same art on front panel) |
| 3 | **Publish** → download wrap / PDF | Saved assets |

**Important:** Full wrap never runs on page load. It starts only after Step 1 succeeds. Stale wraps (from an old front) are hidden until you regenerate.

**Interior PDF:** Export uses the **same HTML/CSS pipeline** as Book Formatter preview (WYSIWYG). Selected interior style, text size, line spacing, and colors in the formatter match the downloaded PDF.

---

## Standard error responses

| HTTP | Meaning | Action |
|------|---------|--------|
| 401 | Invalid or missing `X-API-Key` | Fix key in server env / `appsettings.Local.json` |
| 422 | Invalid JSON or missing required field | Check request body |
| 500 | Server error (often bad `page_count` on legacy wrap APIs) | Retry; check `/api/queue-data` |
| Timeout | Queue busy | Wait; poll `/api/queue-data` |

Successful JSON responses usually include a `status` or payload field; chapter endpoints return generated `content` and `chapter_name`.

---

## How a chapter moves through the system

```
Step 1: Generate  →  saved in Temporary_database (draft)
Step 2: Edit      →  still in Temporary_database (updated)
Step 3: Approve   →  moved to User_confirm (final)
```

Simple picture:

1. User asks AI to write chapter → **generate**
2. User wants changes → **edit**
3. User is happy → **approve** → chapter is final

---

# API Details (one by one)

---

## 1. Generate chapter

**What it does:** AI writes a new chapter for a book.

**URL:** `http://162.229.248.26:8001/api/generate_chapter`  
**Method:** POST  
**Header:** `X-API-Key: YOUR_API_KEY_HERE`  
**Body type:** JSON

**Request:**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "user_input": "how gravity descover"
}
```

**Example response:**

```json
{
  "content": "The gravitational force is invented in 8790...",
  "chapter_name": "The gravitational force is invented in 8790",
  "status": "success"
}
```

Saved to **Temporary_database** until the user approves.

---

| Field | Meaning |
|-------|---------|
| `user_id` | User ID (example: u123) |
| `book_id` | Book ID (example: b456) |
| `chapter` | Chapter number as text (example: "18") |
| `user_input` | What you want the chapter to be about |

**Example result:**  
The heading might come back like: *"The gravitational force is invented in 8790"*

**Important:**
- This takes **several minutes**. Do not refresh the page too early.
- Data is saved in table **Temporary_database** until the user approves.

---

## 2. Edit chapter

**What it does:** Change text in a chapter that is still a draft.

**URL:** `http://162.229.248.26:8001/api/edit`  
**Method:** POST

**Send this:**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "change the title something else"
}
```

**Example — change 8790 to 6789 in the heading:**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": "18",
  "changes": "replace in heading 8790 with 6789"
}
```

| Field | Meaning |
|-------|---------|
| `changes` | Tell the AI what to change in plain English |

---

## 3. Audio to text

**What it does:** Upload or point to an audio file. AI turns speech into text.

**URL:** `http://162.229.248.26:8001/api/audio`  
**Method:** POST

**Option A — send file path (path must exist on the API server):**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 14,
  "audio_file_path": "C:\\book_project\\Audio_transcribe_into_text\\John s Morning Routi.mp3"
}
```

**Option B — upload file through the website (recommended)**  
Use the EbookAI app screen. The website sends the file for you.

**Supported files:** `.mp3` `.mp4` `.mpeg` `.mpga` `.m4a` `.wav` `.webm`

**Saved in table:** `audio_transcriptions`

---

## 4. Approve chapter

**What it does:** User says "yes, this chapter is final."  
Draft moves from **Temporary_database** → **User_confirm**.

**URL:** `http://162.229.248.26:8001/api/approve`  
**Method:** POST  
**Speed:** Fast (seconds)

**Correct body:**

```json
{
  "user_id": "u123",
  "book_id": "b456",
  "chapter": 18,
  "approve": true
}
```

**Wrong — do NOT do this (space in key name breaks JSON):**

```json
{
  "chapter ": 18
}
```

Use `chapter` not `chapter ` (no space after the word).

---

## 5. Queue status

**What it does:** Shows how many jobs are running and how many people are waiting.

**URL:** `http://162.229.248.26:8001/api/queue-data`  
**Method:** GET  
**Body:** None  
**Speed:** Fast

**Example answer:**

```json
{
  "status": {
    "running": 2,
    "waiting": 10,
    "max_concurrent": 20,
    "total_requests": 50
  }
}
```

| Field | Meaning |
|-------|---------|
| `running` | Jobs working right now |
| `waiting` | Jobs in line |
| `max_concurrent` | Max jobs at same time |
| `total_requests` | Total jobs tracked |

**Tip:** If `waiting` is very high, generate/edit may take longer.

---

## 6. Generate cover

**What it does:** AI makes a front cover image for a book.

**URL:** `http://162.229.248.26:8001/api/generate-cover`  
**Method:** POST

**Send this:**

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

| Field | Meaning |
|-------|---------|
| `title` | Book title |
| `author_name` | Author name |
| `category` | Book category |
| `cover_style` | Art style you want |
| `size` | Image size (see list at top) |
| `quality` | low / medium / high / auto |

---

## 7. Edit cover

**What it does:** Change an existing cover image using AI.

**URL:** `http://162.229.248.26:8001/api/edit-cover`  
**Method:** POST

**Send this:**

```json
{
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA...",
  "image_direction": "make the sky brighter and add more stars",
  "size": "1024x1536"
}
```

| Field | Meaning |
|-------|---------|
| `encoded_image` | The image as base64 text (the image you want to edit) |
| `image_direction` | Tell AI what to change |
| `size` | Output size |

---

## 8. Suggest chapter names

**What it does:** AI suggests names for the next chapter based on past chapters.

**URL:** `http://162.229.248.26:8001/api/book_chapters_name`  
**Method:** POST

**Send this:**

```json
{
  "user_id": "u1",
  "book_id": "b1",
  "highlights": [
    {
      "chapter_name": "Chapter 1",
      "detailed_bullet_summary": "Hero learns about gravity."
    }
  ]
}
```

AI may return about **5 name ideas**. User picks one.  
Saved in **Temporary_database** column `suggest_chapter_name`.

---

## 9. Refine cover prompt

**What it does:** Takes your rough cover idea and makes a better prompt for the AI.

**URL:** `http://162.229.248.26:8001/api/refine_cover_prompt`  
**Method:** POST  
**Speed:** Fast

**Send this:**

```json
{
  "user_prompt": "here is the prompt"
}
```

---

## 10. Suggest cover prompt from highlights

**What it does:** Builds a cover prompt from your chapter summaries.

**URL:** `http://162.229.248.26:8001/api/suggest-cover-prompt-from-highlights`  
**Method:** POST

**Send this:**

```json
{
  "user_id": "u1",
  "book_id": "b1",
  "highlights": [
    {
      "chapter_name": "Chapter 1",
      "detailed_bullet_summary": "A boy discovers a hidden world."
    },
    {
      "chapter_name": "Chapter 2",
      "detailed_bullet_summary": "He learns to fly for the first time."
    }
  ]
}
```

---

## 11. Generate full print cover (spine + back + front)

**What it does:** Makes a full wrap cover for paperback printing (KDP).

**URL:** `http://162.229.248.26:8001/api/generate-spine-book-cover`  
**Method:** POST

**Send this (exact payload):**

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

| Field | Meaning |
|-------|---------|
| `title` | Book title |
| `author_name` | Author name on cover |
| `size` | Image size — `1536x1024` (landscape) for wrap |
| `quality` | `low`, `medium`, `high`, or `auto` |
| `Interior_trim_size` | Trim size (example: `6 x 9 in`) — note capital `I` |
| `page_count` | Interior page count (spine width) — keep 24–100 |
| `paper_type` | `white` or `cream` |

**Note:** This endpoint invents front + spine + back together — the front will NOT match a cover made earlier with `/api/generate-cover`. For a wrap that keeps your existing front, use endpoint 12 (split).

---

## 12. Generate print wrap from front cover (split)

**What it does:** Builds spine + back + full wrap for paperback **from a saved front cover**. The front panel in the wrap is the same image you generated in Step 1 (`encoded_image`).

**URL:** `http://162.229.248.26:8001/api/generate-spine-book-cover-split`  
**Method:** POST  
**Header:** `X-API-Key: your-key`  
**Speed:** Slow (~1–2 minutes when queue is idle)

**Send this:**

```json
{
  "title": "The Iqbal Day",
  "author_name": "Sara Khan",
  "encoded_image": "iVBORw0KGgoAAAANSUhEUgAA...",
  "size": "1536x1024",
  "quality": "high",
  "Interior_trim_size": "6 x 9 in",
  "paper_type": "white",
  "page_count": 40
}
```

| Field | Meaning |
|-------|---------|
| `title` | Book title |
| `author_name` | Author name on cover |
| `encoded_image` | **Required** — raw Base64 of the saved front cover from Cover Design Step 1 (no `data:image/...` prefix) |
| `size` | Image size (`1536x1024` is typical for wrap) |
| `quality` | `low`, `medium`, `high`, or `auto` |
| `Interior_trim_size` | Trim size (example: `6 x 9 in`) |
| `paper_type` | `white`, `cream`, or color paper token |
| `page_count` | Interior page count for spine width — use **24–100** on live server |

**Success response:** JSON includes `full_cover_base64` (full wrap image). The EbookAI app saves this to your book settings.

**When to use split vs full (`generate-spine-book-cover`):**

| Endpoint | When |
|----------|------|
| `generate-cover` | Cover Design **Step 1** — front cover only |
| `generate-spine-book-cover-split` | Cover Design **Step 2** — wrap from saved front + calculated spine |
| `generate-spine-book-cover` | Legacy — generates front + spine + back together (may not match Step 1 front) |

---

# Database tables (where data is saved)

These tables live on the Book API server (MySQL).

---

## Table 1: Temporary_database

**Purpose:** Stores draft chapters before the user approves them.

| Column | What it stores |
|--------|----------------|
| `id` | Row number (auto) |
| `user_id` | User |
| `book_id` | Book |
| `chapter` | Chapter number |
| `chapter_name` | Chapter title |
| `user_input` | What user asked for |
| `content` | Full chapter text |
| `suggest_chapter_name` | AI suggests ~5 names; user picks one |
| `highlight_of_previous_chapter` | Short summary of last chapter |
| `date` | Date saved |
| `time` | Time saved |

---

## Table 2: User_confirm

**Purpose:** Stores chapters the user has approved (final).

Same columns as Temporary_database, **except** no `suggest_chapter_name`.

---

## Table 3: audio_transcriptions

**Purpose:** Stores audio-to-text results.

| Column | What it stores |
|--------|----------------|
| `id` | Row number |
| `date`, `time` | When saved |
| `user_input` | Transcribed text |
| `book_id` | Book |
| `chapter` | Chapter |
| `user_id` | User |
| `audio_file_path` | Path to audio file |

---

## Table 4: queue_monitor

**Purpose:** Tracks how busy the API is.

| Column | What it stores |
|--------|----------------|
| `status_running` | Jobs running now |
| `status_waiting` | Jobs waiting in line |
| `status_max_concurrent` | Max parallel jobs |
| `status_total_requests` | Total requests |
| `logs` | Text log messages |
| `user_id`, `book_id`, `chapter` | Which job |
| `log_date`, `log_time` | When logged |

---

## Table 5: error_logs

**Purpose:** Stores errors when something goes wrong.

| Column | What it stores |
|--------|----------------|
| `line_number` | Line in code |
| `error` | Error message |
| `filename` | File name |
| `error_date`, `error_time` | When it happened |

---

# Website screens → which API they use

You use the website. The website calls the Book API for you.

| Screen / action | Website does this |
|-----------------|---------------------|
| AI Writer → Generate | Calls `/api/generate_chapter` |
| AI Writer → Edit | Calls `/api/edit` |
| Approve chapter | Calls `/api/approve` |
| Upload audio | Calls `/api/audio` |
| Cover Design → Generate Cover | Calls `/api/generate-cover`; then builds full wrap when format is Paperback/Both |
| Cover Design → Full wrap preview | Chain: split API → `/api/generate-spine-book-cover` → local compositor (front re-synced from wrap) |
| Cover Design → Edit | Calls `/api/edit-cover` |
| Publish → export print pack | Local wrap + Chromium PDF (wrap built on Cover Design) |
| Cover prompt help | Calls `/api/refine_cover_prompt` |
| Chapter name ideas | Calls `/api/book_chapters_name` |

**Live website:** http://138.197.76.70:5000

---

# Quick test (on the server)

Run these on the server console after deploy:

```bash
# Is the website running?
curl -s http://127.0.0.1:5000/health

# Is the API key set?
curl -s http://127.0.0.1:5000/Books/ExternalApiStatus

# Is the Book API reachable?
curl -s -H "X-API-Key: YOUR_KEY" http://162.229.248.26:8001/api/queue-data
```

Replace `YOUR_KEY` with the key from `/etc/default/ebookai`.

**One command to test everything:**

```bash
cd /opt/EbookAI && bash deploy/verify-all-apis.sh
```

---

# Common problems and fixes

| Problem | What to do |
|---------|------------|
| "No API key" error | Add `ExternalApi__ApiKey` in `/etc/default/ebookai`, restart app |
| Generate takes very long | Normal — wait 5+ minutes. Check queue with `/api/queue-data` |
| Many people waiting in queue | Wait and try again later |
| Login stops working after deploy | Log in again once |
| Audio fails | Upload file through website, do not use a path on your PC |
| Cover edit fails | Check image is valid base64 and size is correct |
| Full wrap never appears on Publish | Complete Cover Design Step 1 (front), then Step 2 (full wrap). Set `ExternalApi:ApiKey`. Keep `page_count` ≤ 100. |
| Wrap API returns 500 | Often caused by `page_count` above ~100 — lower page count or open Book Formatting to refresh page estimate |
| 401 error | Wrong API key — fix key in `/etc/default/ebookai` |

---

# Deploy (keep the app updated)

**Server path:** `/root/latest/EbookAI`  
**Live site:** http://138.197.76.70:5000

### One-shot deploy (recommended)

Run on the server. Replace `PASTE_YOUR_API_KEY_HERE` with your trimmed `X-API-Key` (no spaces):

```bash
APP_DIR=/root/latest/EbookAI
ENV_FILE=/etc/default/ebookai

# 1) API key + env (required — app crashes without ExternalApi__ApiKey)
if [ ! -f "$ENV_FILE" ]; then
  cp "$APP_DIR/deploy/etc-default-ebookai.example" "$ENV_FILE"
fi
grep -q '^ExternalApi__ApiKey=' "$ENV_FILE" \
  && sed -i 's|^ExternalApi__ApiKey=.*|ExternalApi__ApiKey=PASTE_YOUR_API_KEY_HERE|' "$ENV_FILE" \
  || echo 'ExternalApi__ApiKey=PASTE_YOUR_API_KEY_HERE' >> "$ENV_FILE"
chmod 600 "$ENV_FILE"

# 2) Pull + publish + restart
cd "$APP_DIR"
git pull origin Clean_Code
dotnet publish -c Release -r linux-x64 --self-contained true -maxcpucount:1 -o publish
kill -9 $(netstat -tpln | awk '/:5000/ {print $7}' | cut -d'/' -f1 | head -1) 2>/dev/null
fuser -k 5000/tcp 2>/dev/null
sleep 2
cd publish
set -a && source "$ENV_FILE" && set +a
export ASPNETCORE_ENVIRONMENT=Production
: > ../nohup.out
nohup dotnet EBookDashboard.dll --urls http://0.0.0.0:5000 >> ../nohup.out 2>&1 &
sleep 6
curl -s -o /dev/null -w "HTTP %{http_code}\n" http://127.0.0.1:5000/
tail -25 ../nohup.out
```

Expect `HTTP 200`. If you see `ExternalApi:ApiKey is not set`, the key line in `/etc/default/ebookai` is missing or empty.

### Alternative (uses project deploy scripts)

```bash
cd /root/latest/EbookAI
APP_DIR=/root/latest/EbookAI bash deploy/do-deploy.sh
```

---

# More files in this project

| File | What it is |
|------|------------|
| `smoke-tests.http` | Click-to-test all APIs in VS Code |
| `Scripts/smoke_test.py` | Test script |
| `Scripts/smoke-payloads/` | Sample JSON files |
| `deploy/etc-default-ebookai.example` | Server settings example |
| `http://138.197.76.70:5000/Books/ApiDocumentation` | Live JSON list of all APIs |

---

*Last updated Jul 2026 (Clean_Code branch) — verified against the latest endpoint spec. Keep your API key secret — never paste it in chat, commits, or client-side code; if it leaks, rotate it on the Book API server and update `/etc/default/ebookai`.*
