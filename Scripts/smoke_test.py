#!/usr/bin/env python3
"""
Upstream book API smoke test.

Usage:
  python Scripts/smoke_test.py --base-url http://162.229.248.26:8001 --api-key "$API_KEY"

Environment fallbacks:
  API_BASE_URL, API_KEY
"""

from __future__ import annotations

import argparse
import json
import os
import time
from dataclasses import dataclass
from typing import Any, Dict, Optional, Sequence

import httpx


@dataclass
class EndpointCheck:
    name: str
    method: str
    path: str
    body: Optional[Dict[str, Any]]
    expected_statuses: Sequence[int]
    note: str = ""


CHECKS: list[EndpointCheck] = [
    EndpointCheck(
        name="queue-data",
        method="GET",
        path="/api/queue-data",
        body=None,
        expected_statuses=[200],
        note="Queue monitor endpoint should always be available.",
    ),
    EndpointCheck(
        name="generate_chapter",
        method="POST",
        path="/api/generate_chapter",
        body={
            "user_id": "u123",
            "book_id": "b456",
            "chapter": "18",
            "user_input": "how gravity discover",
        },
        expected_statuses=[200],
        note="May take longer than other calls.",
    ),
    EndpointCheck(
        name="edit",
        method="POST",
        path="/api/edit",
        body={
            "user_id": "u123",
            "book_id": "b456",
            "chapter": "18",
            "changes": "replace 8790 with 6789 in heading",
        },
        expected_statuses=[200],
    ),
    EndpointCheck(
        name="audio",
        method="POST",
        path="/api/audio",
        body={
            "user_id": "u123",
            "book_id": "b456",
            "chapter": 14,
            "audio_file_path": "/tmp/non-existent-audio.mp3",
        },
        expected_statuses=[200, 400, 422],
        note="This smoke uses JSON path mode. 400/422 is acceptable for invalid file path.",
    ),
    EndpointCheck(
        name="approve",
        method="POST",
        path="/api/approve",
        body={
            "user_id": "u123",
            "book_id": "b456",
            "chapter": 18,
            "approve": True,
        },
        expected_statuses=[200],
    ),
    EndpointCheck(
        name="generate-cover",
        method="POST",
        path="/api/generate-cover",
        body={
            "title": "The Power of Gravity",
            "author_name": "Hasan Rahim",
            "category": "Science",
            "cover_style": "Modern Illustration",
            "size": "1024x1536",
            "quality": "medium",
        },
        expected_statuses=[200],
    ),
    EndpointCheck(
        name="generate-spine-book-cover",
        method="POST",
        path="/api/generate-spine-book-cover",
        body={
            "title": "The Light Keeper",
            "author_name": "Christina Wallace",
            "category": "Fantasy / Adventure",
            "cover_style": (
                "Deep navy blue background with subtle damask pattern, ornate gold baroque "
                "decorative frame on front cover, elegant gold serif typography, luxurious premium publishing style"
            ),
            "size": "1536x1024",
            "quality": "medium",
            "Interior_trim_size": "6 x 9 in",
            "page_count": 250,
        },
        expected_statuses=[200],
    ),
    EndpointCheck(
        name="generate-spine-book-cover-split",
        method="POST",
        path="/api/generate-spine-book-cover-split",
        body={
            "title": "The Iqbal Day",
            "author_name": "Sara Khan",
            "encoded_image": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==",
            "size": "1536x1024",
            "quality": "high",
            "Interior_trim_size": "6 x 9 in",
            "paper_type": "white",
            "page_count": 40,
        },
        expected_statuses=[200],
    ),
    EndpointCheck(
        name="edit-cover",
        method="POST",
        path="/api/edit-cover",
        body={
            "encoded_image": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==",
            "image_direction": "brighter foreground",
            "size": "1024x1536",
        },
        expected_statuses=[200],
    ),
    EndpointCheck(
        name="book_chapters_name",
        method="POST",
        path="/api/book_chapters_name",
        body={
            "user_id": "u1",
            "book_id": "b1",
            "highlights": [{"chapter_name": "Intro", "detailed_bullet_summary": "..."}],
        },
        expected_statuses=[200],
    ),
    EndpointCheck(
        name="refine_cover_prompt",
        method="POST",
        path="/api/refine_cover_prompt",
        body={"user_prompt": "mystical forest at dawn"},
        expected_statuses=[200],
    ),
    EndpointCheck(
        name="suggest-cover-prompt-from-highlights",
        method="POST",
        path="/api/suggest-cover-prompt-from-highlights",
        body={
            "user_id": "u1",
            "book_id": "b1",
            "highlights": [{"chapter_name": "Chapter 1", "detailed_bullet_summary": "Hero discovers gravity."}],
        },
        expected_statuses=[200],
    ),
]


def run_check(client: httpx.Client, check: EndpointCheck) -> dict[str, Any]:
    start = time.perf_counter()
    try:
        if check.method == "GET":
            response = client.get(check.path)
        else:
            response = client.request(check.method, check.path, json=check.body)
        latency_ms = round((time.perf_counter() - start) * 1000, 2)
        snippet = response.text.strip().replace("\n", " ")
        if len(snippet) > 220:
            snippet = snippet[:220] + "..."
        success = response.status_code in check.expected_statuses
        return {
            "endpoint": check.path,
            "method": check.method,
            "status": response.status_code,
            "ok": success,
            "latency_ms": latency_ms,
            "note": check.note,
            "response_snippet": snippet,
        }
    except Exception as exc:  # pragma: no cover - smoke script should never throw
        latency_ms = round((time.perf_counter() - start) * 1000, 2)
        return {
            "endpoint": check.path,
            "method": check.method,
            "status": "EXCEPTION",
            "ok": False,
            "latency_ms": latency_ms,
            "note": check.note,
            "response_snippet": str(exc),
        }


def print_report(results: list[dict[str, Any]]) -> None:
    print("| Endpoint | Method | Status | Result | Latency (ms) |")
    print("|---|---|---:|---|---:|")
    for row in results:
        badge = "PASS" if row["ok"] else "FAIL"
        print(f"| `{row['endpoint']}` | `{row['method']}` | `{row['status']}` | {badge} | {row['latency_ms']} |")
    print()
    for row in results:
        if row["note"] or not row["ok"]:
            print(f"- `{row['endpoint']}`: {row['response_snippet']}")


def main() -> int:
    parser = argparse.ArgumentParser(description="Smoke-test upstream book APIs")
    parser.add_argument("--base-url", default=os.getenv("API_BASE_URL", "http://162.229.248.26:8001"))
    parser.add_argument("--api-key", default=os.getenv("API_KEY", ""))
    parser.add_argument("--timeout-seconds", type=float, default=90.0)
    parser.add_argument("--json-output", default="", help="Optional file path for raw JSON results")
    args = parser.parse_args()

    if not args.api_key:
        print("ERROR: Missing API key. Set API_KEY env var or pass --api-key.")
        return 2

    headers = {"X-API-Key": args.api_key}
    results: list[dict[str, Any]] = []

    with httpx.Client(base_url=args.base_url.rstrip("/"), headers=headers, timeout=args.timeout_seconds) as client:
        for check in CHECKS:
            results.append(run_check(client, check))

    print_report(results)

    if args.json_output:
        with open(args.json_output, "w", encoding="utf-8") as fp:
            json.dump(results, fp, indent=2)
        print(f"\nSaved JSON report to: {args.json_output}")

    return 0 if all(r["ok"] for r in results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
