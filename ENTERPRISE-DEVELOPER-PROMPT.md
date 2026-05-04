# Enterprise Developer Prompt — AI Ebook Publishing SaaS

Use this document in **Cursor** or hand off to a developer. Implement everything **inside the existing project**; do not create a new solution.

**See also:** `API-DOCUMENTATION.md` for external API details, request/response shapes, and database tables.

---

## Rules

- **Do NOT create a new project.** Work inside the current codebase.
- **Keep existing UI:** fonts, colors, spacing, inputs. No extra visible inputs; use **modals** or backend for new features.
- **No new database columns/tables** unless explicitly required below; use session and existing tables where possible.

---

## Core User Workflow (Strict Order)

Users must follow this order. Steps cannot be skipped.

| Step | Action |
|------|--------|
| 1 | AI Book Generation |
| 2 | Book Formatting |
| 3 | Cover Design |
| 4 | Publish Book |
| 5 | Stripe Payment |
| 6 | Admin Approval |

**Unlock flow:**

- **AI Generation** → Unlock **Formatting**
- **Formatting** done → Unlock **Cover**
- **Cover** finalized → Unlock **Publish**

---

## Sidebar Navigation Logic

| Page | When enabled |
|------|----------------|
| AI Generate Book | Always |
| Book Formatting | After first AI book generated |
| AI Cover Design | After Formatting done |
| My Books / Publish | After Cover finalized |
| Support, Audio Book | After first AI book (or same as Formatting) |

**Locked pages:** Show modal: *"Please complete the previous step before accessing this section."* Disable and blur main content. Offer button to go to the correct step.

---

## AI Book Generation APIs

**Base URL:** `http://162.229.248.26:8001`  
**Header:** `X-API-Key: AK-proj-c8r15p0EYc1B0SKi5_hP58HEyL6xP0ywmZ2hEpvpvU5y-i7yZ8IiyLv1K7cGSkyNh`

### 1. Generate Chapter  
`POST /api/generate_chapter`  
Body: `{ "user_id", "book_id", "chapter", "user_input" }`  
Result appears in chapter editor panel.

### 2. Edit Chapter  
`POST /api/edit`  
Body: `{ "user_id", "book_id", "chapter", "changes" }`  
Show **side-by-side**: Original (left) | Edited (right). User confirms.

### 3. Audio Transcription  
`POST /api/audio`  
Body: `user_id`, `book_id`, `chapter`, `audio_file_path`  
Formats: .mp3, .mp4, .mpeg, .mpga, .m4a, .wav, .webm  
Result populates chapter editor.

### 4. Chapter Approval  
`POST /api/approve`  
Body: `{ "user_id", "book_id", "chapter", "approve": true }`  
Moves chapter from **Temporary** → **Confirmed** DB.

### 5. Queue Monitoring  
`GET /api/queue-data`  
Use for real-time processing indicator, e.g. *"3 requests running. 7 users waiting."*

---

## AI Cover Design APIs

### Generate Cover  
`POST /api/generate-cover`  
Body: `{ "title", "author_name", "category", "cover_style", "size", "quality" }`  
Sizes: `1024x1024`, `1536x1024`, `1024x1536`, `auto`  
Quality: `low`, `medium`, `high`, `auto`  
Show result in Book Preview UI.

### Edit Cover  
`POST /api/edit-cover`  
Body: `{ "encoded_image", "image_direction", "size" }`  
Used for AI editing of generated covers.

### Chapter Name Suggestion  
`POST /api/book_chapters_name`  
Returns 5 suggested chapter names; user picks one; save in DB.

---

## Database (External / Reference)

- **Temporary_database:** AI content before approval. Columns: id, user_id, book_id, chapter, chapter_name, user_input, content, suggest_chapter_name, highlight_of_previous_chapter, date, time.
- **User_confirm:** Approved chapters. Same structure minus suggest_chapter_name.
- **audio_transcriptions:** Audio-to-text results.
- **queue_monitor:** status_running, status_waiting, etc.
- **error_logs:** line_number, error, filename, date, time. Log every API failure.

---

## Book Cover Features

1. **Upload Cover** — User can upload own cover instead of AI.
2. **Edit criteria** — Allow editing cover prompt.
3. **Publish redirect** — Publish button → Stripe checkout.

---

## Stripe Payment

- **Where:** Book Cover final page, EbookHub.
- **Flow:** User clicks **Publish Book** → redirect to Stripe checkout. On success: Book status = **Paid**, **Ready for Publishing**. Admin panel reflects this.

---

## Admin Dashboard

- List: Users, Books, Chapters, Payments, Publishing status.
- Actions: Approve or reject books.

---

## EbookHub Page

- Show: Books, Authors, Categories, Trending books (dummy data acceptable).
- Integrate Stripe where needed.

---

## Block Option

- Screen: Block content or users (dummy data OK).
- Admin sees: Blocked books, Blocked users.

---

## Support Page

- **AI support assistant:** Answer publishing questions, guide for Amazon KDP, guide for Wattpad.

---

## Amazon Publishing Support

- Tutorial: *"How to publish on Amazon KDP"*
- Steps: Book formatting, Cover requirements, Upload, Pricing, Publishing.

---

## ElevenLabs

- Voice generation for audiobook (Amazon audiobook compatible). Convert chapters to audiobook.

---

## UI Quality

- Do **not** change fonts, colors, inputs, spacing, buttons.
- Use: loading animations, progress bars, modals, smooth transitions.
- Book preview style must be **identical** across all pages.

---

## Logging

Log: user actions, API requests, API failures, queue status, publishing events, Stripe events.  
Store: User ID, Book ID, Chapter, Action, Timestamp.

---

## Final Goal

A complete AI Ebook Publishing SaaS with workflow:  
AI Book Creation → Chapter Editing → Audio to Text → Chapter Approval → Book Formatting → AI Cover → Cover Edit → Publish → Stripe → Admin Approval → Amazon/Wattpad guidance.  
Premium look and professional flow.
