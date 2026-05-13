# Comprehensive E-Book Platform Development Prompt for Cursor AI

Use this document in **Cursor** to implement or extend the e-book platform. Implement **inside the existing ASP.NET Core MVC project** (Razor views, Controllers, existing DB). Do **not** create a new React/Next.js or Node app.

**Tech stack (actual):** ASP.NET Core MVC, Razor views, Entity Framework, existing SQL/MySQL DB, Stripe.NET, existing APIs.  
**Do not use:** React/Next.js frontend, Node/Express backend, MongoDB (unless already in use). Map API endpoints below to existing Controllers and Views.

**See also:** `ENTERPRISE-DEVELOPER-PROMPT.md`, `API-DOCUMENTATION.md`.

---

## Project Overview

Develop a complete e-book platform with AI writer, book formatting, cover design, audiobook generation, and publishing support.

---

## CRITICAL WORKFLOW & PAYMENT INTEGRATION

### Primary User Flow

1. User completes **AI Writer** content generation
2. **Automatic transition** to **Book Formatting** page after completion
3. **Book Cover Design** completion
4. **PAYMENT GATEWAY (Stripe) – MANDATORY CHECKPOINT**
   - User **MUST** complete payment before downloading any files
   - Payment required at two points:
     - After Book Cover Design completion
     - At E-book Hub (My Books) when accessing downloads
5. After payment: allow downloads and access to all features

### Book Formatting Page Requirements

**Location:** `https://localhost:7191/BookDesign/CoverDesignCalculatorFixing`

**Required inputs (all must be functional and reflected in Book Preview):**

- **Select Book** dropdown: “Select a book to format...”
- **Book Format:** Ebook (radio/selection: Ebook, Print, Both)
- **Interior Style:** Novel (dropdown: Novel, Non-Fiction, Children's, Comic)
- **Text Size:** Medium (Small / Medium / Large)
- **Publishing Platforms** (multi-select):
  - Amazon KDP  
  - IngramSpark  
  - Draft2Digital  
  - Smashwords  

**CRITICAL:** All inputs must **dynamically update the Book Preview in real time** (summary line + left/right preview panels).

---

## API INTEGRATIONS

### 1. Book Cover API

- Use existing Book Cover API integration (see `API-DOCUMENTATION.md`)
- Admin portal must be connected to user portal
- Book cover preview style must be **consistent across all pages**

### 2. Audiobook – ElevenLabs API

- **API:** ElevenLabs (not OpenAI; Amazon allows ElevenLabs for AI audiobooks)
- **Features to implement:**
  - Narrator selection (male/female voices)
  - Language options
  - Accent choices (Pakistani English, Indian English, American, British, etc.)
  - Age range (20–30, 30–40, 40–50, etc.)
  - Voice characteristics (warm, energetic, calm, etc.)
  - Emotion controls for narration
  - Pause and laugh insertions for comedy
- **Cost:** Paid API; implement usage tracking

### 3. Stripe Payment

- **Integration points:** After Cover Design completion; at E-book Hub when accessing downloads
- **Flow:** Prevent downloads before payment; lock premium features until paid; show payment page/modal before:
  - Book downloads (PDF, EPUB, MOBI)
  - Audiobook generation
  - Publishing assistance

---

## UI/UX CONSISTENCY

### Design system

- **Font family:** Same as rest of platform (e.g. Inter, Playfair Display, Merriweather as used in views)
- **Colors:** Match existing platform (purple/slate theme)
- **Inputs:** Same style, border, padding as Book Formatting page
- **Buttons:** Consistent size, color, hover
- **Spacing:** Match padding/margins from other pages
- **Icons:** Same library (e.g. Font Awesome, Lucide) and style

### Audiobook screen

- Apply the same design system (inputs, buttons, spacing, fonts, colors).

### Profile page

- **Current issue:** Profile looks like a CV/Resume  
- **Required:** Modern, card-based layout; clear hierarchy  
- **Features:**
  - Circular profile image placeholder
  - Upload/change photo button
  - Optional image cropping
  - Account/login integration, password change, user settings
  - Display name, email, account type, subscription/plan

### Preview consistency

- Book Preview must use the **same design style** on:
  - Book Formatting page  
  - Cover Design page  
  - E-book Hub / My Books  
  - Publishing page  

---

## PUBLISHING SUPPORT

### Option A: Self-publish (external platforms)

- **Platforms:** Amazon KDP, Barnes & Noble Press, IngramSpark, Lulu, Draft2Digital, Smashwords  
- **Per platform:** Step-by-step tutorial videos (YouTube embeds), PDF guide downloads, requirements checklist

### Option B: Publish through our platform

- We handle publishing (longer process)
- Book gets 10,000+ initial traffic boost
- Revenue support; platform manages distribution

### Tutorial section structure

- Section title: “How to Publish Your Book”
- **Tutorial cards** per platform (e.g. Amazon KDP, Barnes & Noble, IngramSpark, Lulu, Draft2Digital):
  - Title (e.g. “Amazon KDP Tutorial”)
  - Embedded YouTube video
  - “Download PDF Guide” link

### AI Publishing Assistant (support chat)

- AI chatbot for publishing questions
- **Topics:** KDP account creation, book sizes (6×9, 8.5×11), formatting, ISBN, pricing, categories, keywords
- ChatGPT-style UI; context-aware answers; step-by-step guidance; optional screenshots/visuals  
- **Example queries:** “How do I create an Amazon KDP account?”, “What book size for a novel?”, “How do I add tax information to KDP?”, “What are the 3 options after signing into KDP?”

---

## ADDITIONAL FEATURES

### Book cover upload

- Allow **upload own cover** OR use platform cover design tool
- Formats: JPG, PNG, PDF
- Size/DPI: specify dimensions; minimum 300 DPI where applicable

### E-book Hub dummy data

- Sample/dummy books in E-book Hub
- Example listings, sample formatting options, mock previews, dummy reviews/ratings

### Account management

- If user has account → login
- If not → Create account → complete profile → follow flow
- Profile: creation/login, profile picture, password, preferences, subscription

---

## TECHNICAL SPECIFICATIONS (map to existing stack)

**Actual stack:** ASP.NET Core MVC, Razor, EF Core, existing DB, Stripe.NET.  
Map the following to **existing** Controllers and Views (not new React/Node).

### Endpoints (conceptual → existing or new actions)

| Concept | Map to |
|--------|--------|
| Format book | `BookDesign/SaveBookFormatting`, formatting view |
| Generate/upload cover | `Books/GenerateAICoverPreview`, `Books/FinalizeCover`, Cover Design view |
| Book preview | `Books/GetFullBookContent`, formatting/cover preview partials |
| Payment checkout | `Checkout/CreateBookCheckoutSession`, `Checkout/BookPayment` |
| Payment verify/status | `Checkout/BookPaymentSuccess`, `Books/GetBookPaymentStatus` |
| Audiobook generate | New or existing audiobook controller + ElevenLabs |
| Audiobook voices/settings | New endpoints + ElevenLabs API |
| Publishing tutorials | New view + optional API for tutorials list |
| Support chat | New support chat endpoint + AI (e.g. OpenAI) |
| User upload photo / update profile / change password | Account/Profile controller and views |

### File structure (existing project)

- **Controllers:** `BooksController`, `BookDesignController`, `DashboardController`, `CheckoutController`, Account, etc.
- **Views:** `Views/Books/`, `Views/BookDesign/`, `Views/Dashboard/`, `Views/Checkout/`, `Views/Account/`, `Views/Shared/`
- **Components:** Implement in Razor partials, JS in views or shared scripts; no separate React `/components` folder unless you add a React SPA later.

---

## STYLING (align with existing)

- Use existing CSS/Tailwind classes and design tokens already in the project
- **Profile page:** Card layout, rounded corners, shadow; circular profile image with border (e.g. primary color)
- **Inputs:** Same border, radius, padding as Book Formatting inputs
- **Buttons:** Same primary/secondary styles as rest of app

---

## VALIDATION & ERROR HANDLING

- **Payment:** If not paid, show modal/page “Please complete payment to download your book”; prevent download
- **Book formatting:** If no book selected, show error “Please select a book to format”
- **Profile picture:** Allowed formats e.g. image/jpeg, image/png; max size e.g. 5MB; clear error messages

---

## PRIORITY IMPLEMENTATION ORDER

1. **Phase 1 – Core flow**  
   Book Formatting with all inputs; Book Preview real-time updates; flow AI Writer → Formatting → Cover Design.

2. **Phase 2 – Payment**  
   Stripe at both checkpoints; payment gate before downloads; verification (e.g. `Book.Status = "Paid"`).

3. **Phase 3 – APIs**  
   ElevenLabs audiobook; Book Cover API; admin–user portal connection.

4. **Phase 4 – UI/UX**  
   Standardize Audiobook screen; redesign Profile (modern, not CV); preview consistency everywhere.

5. **Phase 5 – Publishing**  
   Tutorial videos; AI support chatbot; platform selection (self-publish vs our platform).

6. **Phase 6 – Extras**  
   Profile picture upload; E-book Hub dummy data; book cover upload option.

---

## TESTING CHECKLIST

- [ ] All Book Formatting inputs update preview in real time
- [ ] Payment blocks downloads until completed
- [ ] Stripe works at both checkpoints (after cover; at E-book Hub)
- [ ] ElevenLabs audiobook generation and voice options work
- [ ] Profile picture upload (and optional crop) works
- [ ] Profile UI is modern (not CV-style)
- [ ] Audiobook screen matches design system
- [ ] Tutorial videos embed correctly
- [ ] AI support chatbot answers publishing questions
- [ ] Book cover upload accepts valid formats
- [ ] Admin portal connected to user portal
- [ ] Preview style consistent across pages
- [ ] Dummy data displays in E-book Hub

---

## Current implementation status (reference)

- **Book Formatting:** Select Book, Format, Interior Style, Text Size, Publishing Platforms; real-time preview summary and panels; save to DB; “Next: Cover Design” → Cover Design page.
- **Payment gate:** After finalize cover → redirect to `Checkout/BookPayment`; My Books “Download / Publish” → Book Payment if not paid, `Books/BookDownloads` if paid; `Book.Status = "Paid"` set in `Checkout/BookPaymentSuccess`; `GenerateFormats` (EPUB/PDF) requires paid.
- **Stripe:** `CheckoutController`: `BookPayment(bookId)`, `CreateBookCheckoutSession`, `BookPaymentSuccess`; optional `BookPayment:AmountCents` and `BookPayment:Currency` in config.
- **Unlock flow:** Session flags for FormattingDone, CoverFinalized; locked overlay and sidebar behavior in `_DashboardLayout`.
- **APIs:** See `API-DOCUMENTATION.md` for generate/edit/approve chapter, cover, audio, queue, etc.

**Still to do (from this prompt):** Automatic transition AI Writer → Formatting; ElevenLabs audiobook UI + API; Profile redesign + profile picture upload; Publishing tutorials + AI support chat; Book cover upload; E-book Hub dummy data; full UI/UX alignment (Audiobook, Profile, preview consistency).

---

**END OF PROMPT**
