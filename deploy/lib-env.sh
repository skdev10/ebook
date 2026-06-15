#!/usr/bin/env bash
# Shared helpers for /etc/default/ebookai.
# Values may contain spaces — unquoted lines break bash `source` (e.g. "Publisher: command not found").

fix_env_file_syntax() {
  local f="${1:-/etc/default/ebookai}"
  [[ -f "$f" ]] || return 0
  local changed=0

  # Known bad line from etc-default-ebookai.example (unquoted space).
  if grep -qE '^Email__SenderName=eBook Publisher$' "$f" 2>/dev/null; then
    sed -i "s|^Email__SenderName=eBook Publisher|Email__SenderName='eBook Publisher'|" "$f"
    changed=1
  fi

  # Quote any Email__SenderName value that contains spaces but is not already quoted.
  if grep -qE "^Email__SenderName=[^\"'].*[[:space:]].*" "$f" 2>/dev/null; then
    local val
    val="$(grep -m1 '^Email__SenderName=' "$f" | cut -d= -f2- | tr -d '\r')"
    sed -i "s|^Email__SenderName=.*|Email__SenderName=$(printf '%q' "$val")|" "$f"
    changed=1
  fi

  if [[ "$changed" -eq 1 ]]; then
    echo "    Fixed env file quoting in $f"
  fi
}

# Read a single KEY=value without sourcing the whole file (safe for values with spaces).
env_file_get() {
  local f="$1" key="$2"
  local line val
  line="$(grep -m1 "^${key}=" "$f" 2>/dev/null || true)"
  [[ -n "$line" ]] || return 0
  val="${line#*=}"
  val="${val%$'\r'}"
  if [[ "$val" =~ ^\".*\"$ ]]; then
    val="${val:1:-1}"
  elif [[ "$val" =~ ^\'.*\'$ ]]; then
    val="${val:1:-1}"
  fi
  printf '%s' "$val"
}

safe_source_env() {
  local f="${1:-/etc/default/ebookai}"
  fix_env_file_syntax "$f"
  set -a
  # shellcheck disable=SC1090
  source "$f"
  set +a
}
