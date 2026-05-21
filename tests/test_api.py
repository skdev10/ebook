import os
from typing import Any

import httpx
import pytest


BASE_URL = os.getenv("API_BASE_URL", "http://127.0.0.1:8001").rstrip("/")
API_KEY = os.getenv("API_KEY", "")


def _headers() -> dict[str, str]:
    return {"X-API-Key": API_KEY}


def _client() -> httpx.Client:
    return httpx.Client(base_url=BASE_URL, headers=_headers(), timeout=90.0)


def _assert_json_response(resp: httpx.Response) -> Any:
    assert resp.headers.get("content-type", "").lower().find("application/json") >= 0, resp.text[:400]
    return resp.json()


@pytest.mark.skipif(not API_KEY, reason="API_KEY env var is required for upstream tests")
def test_queue_data():
    with _client() as c:
        resp = c.get("/api/queue-data")
        assert resp.status_code == 200, resp.text[:400]
        _assert_json_response(resp)


@pytest.mark.skipif(not API_KEY, reason="API_KEY env var is required for upstream tests")
def test_generate_chapter():
    payload = {
        "user_id": "u123",
        "book_id": "b456",
        "chapter": "18",
        "user_input": "how gravity discover",
    }
    with _client() as c:
        resp = c.post("/api/generate_chapter", json=payload)
        assert resp.status_code == 200, resp.text[:500]
        _assert_json_response(resp)


@pytest.mark.skipif(not API_KEY, reason="API_KEY env var is required for upstream tests")
def test_edit_chapter():
    payload = {
        "user_id": "u123",
        "book_id": "b456",
        "chapter": "18",
        "changes": "replace 8790 with 6789 in heading",
    }
    with _client() as c:
        resp = c.post("/api/edit", json=payload)
        assert resp.status_code == 200, resp.text[:500]
        _assert_json_response(resp)


@pytest.mark.skipif(not API_KEY, reason="API_KEY env var is required for upstream tests")
def test_audio_endpoint_contract():
    # JSON path-mode smoke test: endpoint should respond with contract error for fake path,
    # not 404/405/401 when auth is correct.
    payload = {
        "user_id": "u123",
        "book_id": "b456",
        "chapter": 14,
        "audio_file_path": "/tmp/non-existent-audio.mp3",
    }
    with _client() as c:
        resp = c.post("/api/audio", json=payload)
        assert resp.status_code in (200, 400, 422), resp.text[:500]


@pytest.mark.skipif(not API_KEY, reason="API_KEY env var is required for upstream tests")
def test_approve():
    payload = {
        "user_id": "u123",
        "book_id": "b456",
        "chapter": 18,
        "approve": True,
    }
    with _client() as c:
        resp = c.post("/api/approve", json=payload)
        assert resp.status_code == 200, resp.text[:500]
        _assert_json_response(resp)


@pytest.mark.skipif(not API_KEY, reason="API_KEY env var is required for upstream tests")
def test_generate_cover():
    payload = {
        "title": "The Power of Gravity",
        "author_name": "Hasan Rahim",
        "category": "Science",
        "cover_style": "Modern Illustration",
        "size": "1024x1536",
        "quality": "medium",
    }
    with _client() as c:
        resp = c.post("/api/generate-cover", json=payload)
        assert resp.status_code == 200, resp.text[:600]
        _assert_json_response(resp)


@pytest.mark.skipif(not API_KEY, reason="API_KEY env var is required for upstream tests")
def test_generate_spine_book_cover():
    payload = {
        "title": "The Light Keeper",
        "author_name": "Christina Wallace",
        "category": "Fantasy / Adventure",
        "cover_style": (
            "Deep navy blue background with subtle damask pattern, ornate gold baroque decorative frame on front cover, "
            "elegant gold serif typography, luxurious premium publishing style"
        ),
        "size": "1536x1024",
        "quality": "medium",
        "Interior_trim_size": "6 x 9 in",
        "page_count": 250,
    }
    with _client() as c:
        resp = c.post("/api/generate-spine-book-cover", json=payload)
        assert resp.status_code == 200, resp.text[:600]
        _assert_json_response(resp)


@pytest.mark.skipif(not API_KEY, reason="API_KEY env var is required for upstream tests")
def test_edit_cover():
    payload = {
        "encoded_image": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==",
        "image_direction": "brighter foreground",
        "size": "1024x1536",
    }
    with _client() as c:
        resp = c.post("/api/edit-cover", json=payload)
        assert resp.status_code == 200, resp.text[:600]
        _assert_json_response(resp)


@pytest.mark.skipif(not API_KEY, reason="API_KEY env var is required for upstream tests")
def test_book_chapters_name():
    payload = {
        "user_id": "u1",
        "book_id": "b1",
        "highlights": [{"chapter_name": "Intro", "detailed_bullet_summary": "..."}],
    }
    with _client() as c:
        resp = c.post("/api/book_chapters_name", json=payload)
        assert resp.status_code == 200, resp.text[:600]
        _assert_json_response(resp)
