#!/usr/bin/env bash
# Kestrel bind port vs public URL port (nginx may front :5000 → app on :5050).
# Source from deploy scripts: source deploy/resolve-app-ports.sh
set -euo pipefail

PUBLIC_PORT="${PUBLIC_PORT:-5000}"

if [[ -z "${KESTREL_PORT:-}" ]]; then
  if command -v nginx >/dev/null 2>&1; then
    KESTREL_PORT=5050
  else
    KESTREL_PORT="$PUBLIC_PORT"
  fi
fi

export KESTREL_PORT PUBLIC_PORT
