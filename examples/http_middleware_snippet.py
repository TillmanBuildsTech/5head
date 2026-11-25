"""Generic ASGI snippet: after the upstream responds, append a state-api event.

This is the same idea as the LiteLLM callback for a home-grown HTTP proxy.
Put the matter id on the incoming request as the X-Matter-Id header.
"""

from __future__ import annotations

import json
import os
import urllib.request


def _record(matter_id: str, summary: str) -> None:
    base = os.environ.get("STATE_API_URL", "").rstrip("/")
    token = os.environ.get("STATE_AUTH_TOKEN", "")
    if not base or not token or not matter_id:
        return
    body = json.dumps(
        {
            "agent": os.environ.get("STATE_AGENT_NAME") or "http-proxy",
            "kind": "model_response",
            "summary": summary[:500],
            "payload": {},
        }
    ).encode("utf-8")
    request = urllib.request.Request(
        f"{base}/matters/{matter_id}/events",
        data=body,
        method="POST",
        headers={"Authorization": f"Bearer {token}", "Content-Type": "application/json"},
    )
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            response.read()
    except Exception:
        return


class RecordTurnMiddleware:
    def __init__(self, app):  # noqa: ANN001
        self.app = app

    async def __call__(self, scope, receive, send):  # noqa: ANN001
        if scope.get("type") != "http":
            await self.app(scope, receive, send)
            return

        headers = {k.decode("latin1").lower(): v.decode("latin1") for k, v in scope.get("headers") or []}
        matter_id = headers.get("x-matter-id", "")

        async def send_wrapper(message):  # noqa: ANN001
            if message.get("type") == "http.response.start":
                _record(matter_id, f"{scope.get('method', '')} {scope.get('path', '')} -> {message.get('status')}")
            await send(message)

        await self.app(scope, receive, send_wrapper)
