#!/usr/bin/env bash
# Verify upstream FastAPI + EbookAI BFF. Run on EbookAI server:
#   cd /opt/EbookAI && bash deploy/verify-all-apis.sh
set -euo pipefail

APP_DIR="${APP_DIR:-/opt/EbookAI}"
ENV_FILE="${ENV_FILE:-/etc/default/ebookai}"
BFF_PORT="${BFF_PORT:-5000}"
UPSTREAM_BASE="${UPSTREAM_BASE:-http://162.229.248.26:8001}"

cd "$APP_DIR"

if [[ -f "$ENV_FILE" ]]; then
  set -a
  # shellcheck disable=SC1090
  source "$ENV_FILE"
  set +a
fi

API_KEY="$(echo "${ExternalApi__ApiKey:-}" | tr -d '"' | sed 's/^[[:space:]]*//;s/[[:space:]]*$//')"
if [[ -n "${ExternalApi__BaseUrl:-}" ]]; then
  UPSTREAM_BASE="${ExternalApi__BaseUrl%/}"
fi

echo "=== EbookAI BFF ==="
curl -sf --max-time 30 "http://127.0.0.1:${BFF_PORT}/health" | head -c 600 || echo "FAIL health"
echo ""
curl -sf --max-time 30 "http://127.0.0.1:${BFF_PORT}/Books/ExternalApiStatus" | head -c 800 || echo "FAIL ExternalApiStatus"
echo ""
curl -sf --max-time 15 "http://127.0.0.1:${BFF_PORT}/Account/EmailStatus" | head -c 400 || echo "WARN EmailStatus (SMTP may be unconfigured)"
echo ""
curl -sf --max-time 15 "http://127.0.0.1:${BFF_PORT}/Books/ApiDocumentation" | head -c 400 || echo "FAIL ApiDocumentation"
echo ""

if [[ -z "$API_KEY" ]]; then
  echo "ERROR: ExternalApi__ApiKey missing in $ENV_FILE"
  exit 1
fi

echo "=== Upstream queue (fast) ==="
QUEUE_JSON="$(curl -sf --max-time 90 -H "X-API-Key: ${API_KEY}" "${UPSTREAM_BASE}/api/queue-data" || true)"
if [[ -z "$QUEUE_JSON" ]]; then
  echo "FAIL GET ${UPSTREAM_BASE}/api/queue-data (timeout or network)"
  exit 1
fi
echo "$QUEUE_JSON" | head -c 500
echo ""

echo "=== Upstream approve (fast smoke) ==="
APPROVE_CODE="$(curl -s -o /tmp/approve_out.json -w "%{http_code}" --max-time 45 \
  -H "X-API-Key: ${API_KEY}" -H "Content-Type: application/json" \
  -d '{"user_id":"smoke","book_id":"smoke","chapter":1,"approve":true}' \
  "${UPSTREAM_BASE}/api/approve")"
echo "approve HTTP $APPROVE_CODE — $(head -c 200 /tmp/approve_out.json 2>/dev/null || true)"
echo ""

echo "=== Upstream refine_cover_prompt (fast) ==="
REFINE_CODE="$(curl -s -o /tmp/refine_out.json -w "%{http_code}" --max-time 45 \
  -H "X-API-Key: ${API_KEY}" -H "Content-Type: application/json" \
  -d '{"user_prompt":"mystical forest at dawn"}' \
  "${UPSTREAM_BASE}/api/refine_cover_prompt")"
echo "refine HTTP $REFINE_CODE — $(head -c 200 /tmp/refine_out.json 2>/dev/null || true)"
echo ""

echo "OK — queue + fast endpoints reachable. generate_chapter/edit/cover are long-running; use:"
echo "  python3 Scripts/smoke_test.py --base-url ${UPSTREAM_BASE} --api-key \"\$API_KEY\" --timeout-seconds 600"
echo "Full docs: docs/EXTERNAL_API.md  |  JSON catalog: http://127.0.0.1:${BFF_PORT}/Books/ApiDocumentation"
