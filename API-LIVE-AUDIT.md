# Live API Audit (upstream FastAPI)

This report captures a direct live probe of `http://162.229.248.26:8001` using the current endpoint inventory.

## 1) Endpoint inventory verification

Confirmed from live `openapi.json` and app route usage:

- `POST /api/generate_chapter`
- `POST /api/edit`
- `POST /api/audio`
- `POST /api/approve`
- `GET /api/queue-data`
- `POST /api/generate-cover`
- `POST /api/generate-spine-book-cover`
- `POST /api/edit-cover`
- `POST /api/book_chapters_name`

Additional live endpoints present (not in the original 9-item list):

- `POST /api/refine_cover_prompt`
- `POST /api/suggest-cover-prompt-from-highlights`

## 2) Connectivity and auth checks

- `GET /docs` => `200`
- `GET /openapi.json` => `200`
- Invalid key checks for all listed endpoints returned `401` quickly (auth is enforced).

## 3) Live functional smoke results (valid key)

The following was measured with direct `curl` calls and explicit timeout caps:

| Endpoint | Method | Result | Latency / timeout |
|---|---|---|---|
| `/api/queue-data` | GET | `200` | `0.456s` |
| `/api/approve` | POST | `200` | `0.487s` |
| `/api/edit` | POST | timeout | `30s` cap reached |
| `/api/audio` | POST | timeout | `30s` cap reached |
| `/api/generate-cover` | POST | timeout | `30s` cap reached |
| `/api/generate-spine-book-cover` | POST | timeout | `30s` cap reached |
| `/api/edit-cover` | POST | timeout | `30s` cap reached |
| `/api/book_chapters_name` | POST | timeout | `30s` cap reached |
| `/api/refine_cover_prompt` | POST | timeout | `30s` cap reached |
| `/api/suggest-cover-prompt-from-highlights` | POST | timeout | `30s` cap reached |
| `/api/generate_chapter` | POST | timeout | `60s` cap reached |

## 4) Interpretation

The service is reachable and authentication works, but most generation/edit-style endpoints are timing out under live conditions. This points to an upstream processing bottleneck, downstream dependency issue, or worker-level blocking under valid auth context.

## 5) Immediate stabilization checklist

1. Add/verify a FastAPI health route that separately checks:
   - app process alive
   - DB connectivity
   - AI provider connectivity
2. Put the FastAPI app behind a process manager (`systemd`, `supervisor`, or `pm2`) and collect stderr/stdout logs.
3. Add per-endpoint timeout + structured logs (`request_id`, endpoint, upstream latency, status).
4. Add queue depth/worker metrics and a hard timeout guard for long-running generation requests.
5. Run `Scripts/smoke_test.py` on every deployment using environment `API_KEY` and store JSON artifacts.
