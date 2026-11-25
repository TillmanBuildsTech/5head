"""LiteLLM custom logger: append one state-api event for every successful model response.

Register it from the LiteLLM proxy config directory:

    litellm_settings:
      callbacks: litellm_matter_callback.matter_callback

The proxy does not decide the curated memory. It only records that a turn
happened. Agents still call update_matter explicitly when a matter's status
or next step changes. See docs/capture-pattern.md.

Environment:
    STATE_API_URL       base URL of the state API
    STATE_AUTH_TOKEN    bearer token
    STATE_AGENT_NAME    agent name stored on the event (default: litellm-proxy)
    STATE_MATTER_ID     used when the request metadata has no matter_id
"""

from __future__ import annotations

import json
import os
import urllib.error
import urllib.request

try:
    from litellm.integrations.custom_logger import CustomLogger
except Exception:  # pragma: no cover - importable without the package for a syntax check
    class CustomLogger:  # type: ignore[no-redef]
        pass


def _metadata(kwargs: dict) -> dict:
    found: dict = {}
    params = kwargs.get("litellm_params") or {}
    if isinstance(params, dict):
        meta = params.get("metadata") or {}
        if isinstance(meta, dict):
            found.update(meta)
    meta = kwargs.get("metadata") or {}
    if isinstance(meta, dict):
        found.update(meta)
    standard = kwargs.get("standard_logging_object") or {}
    if isinstance(standard, dict):
        meta = standard.get("metadata") or {}
        if isinstance(meta, dict):
            found.update(meta)
    return found


def _response_text(response_obj: object) -> str:
    try:
        choices = getattr(response_obj, "choices", None)
        if choices:
            message = getattr(choices[0], "message", None)
            content = getattr(message, "content", None)
            if isinstance(content, str):
                return content
    except Exception:
        return ""
    return ""


def _post_event(matter_id: str, summary: str, payload: dict) -> None:
    base = os.environ.get("STATE_API_URL", "").rstrip("/")
    token = os.environ.get("STATE_AUTH_TOKEN", "")
    if not base or not token or not matter_id:
        return
    body = json.dumps(
        {
            "agent": os.environ.get("STATE_AGENT_NAME") or "litellm-proxy",
            "kind": "model_response",
            "summary": summary[:500],
            "payload": payload,
        }
    ).encode("utf-8")
    request = urllib.request.Request(
        f"{base}/matters/{matter_id}/events",
        data=body,
        method="POST",
        headers={
            "Authorization": f"Bearer {token}",
            "Content-Type": "application/json",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
            response.read()
    except urllib.error.URLError:
        # A missed log line must not fail the user-facing model call.
        return


class MatterEventCallback(CustomLogger):
    def log_success_event(self, kwargs, response_obj, start_time, end_time):  # noqa: ANN001
        meta = _metadata(kwargs if isinstance(kwargs, dict) else {})
        matter_id = str(meta.get("matter_id") or os.environ.get("STATE_MATTER_ID") or "")
        text = _response_text(response_obj)
        model = ""
        if isinstance(kwargs, dict):
            model = str(kwargs.get("model") or "")
        _post_event(
            matter_id,
            text or f"model response from {model or 'unknown model'}",
            {"model": model, "text": text[:2000]},
        )

    async def async_log_success_event(self, kwargs, response_obj, start_time, end_time):  # noqa: ANN001
        self.log_success_event(kwargs, response_obj, start_time, end_time)


matter_callback = MatterEventCallback()
