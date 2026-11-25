#!/usr/bin/env python3
"""List open matters whose newest event is older than STATE_RECONCILE_MINUTES (default 30).

A periodic sweep. It does not change rows. An operator, or a cron'd agent, reads
the list and calls update_matter / append_event for anything the turn hooks missed.

    STATE_API_URL=http://localhost:8080 STATE_AUTH_TOKEN=... python3 scripts/reconcile.py
"""

from __future__ import annotations

import json
import os
import sys
import urllib.request
from datetime import datetime, timedelta, timezone


def main() -> int:
    base = os.environ.get("STATE_API_URL", "").rstrip("/")
    token = os.environ.get("STATE_AUTH_TOKEN", "")
    if not base or not token:
        print("STATE_API_URL and STATE_AUTH_TOKEN are required", file=sys.stderr)
        return 2
    minutes = int(os.environ.get("STATE_RECONCILE_MINUTES", "30"))
    cutoff = datetime.now(timezone.utc) - timedelta(minutes=minutes)
    request = urllib.request.Request(
        f"{base}/matters?status=open",
        headers={"Authorization": f"Bearer {token}"},
    )
    with urllib.request.urlopen(request, timeout=15) as response:
        matters = json.load(response)
    stale = []
    for matter in matters:
        detail_req = urllib.request.Request(
            f"{base}/matters/{matter['id']}",
            headers={"Authorization": f"Bearer {token}"},
        )
        with urllib.request.urlopen(detail_req, timeout=15) as response:
            detail = json.load(response)
        events = detail.get("events") or []
        newest = events[-1]["createdAt"] if events else detail.get("updatedAt")
        if newest is None:
            stale.append(detail)
            continue
        seen = datetime.fromisoformat(newest.replace("Z", "+00:00"))
        if seen < cutoff:
            stale.append({"id": detail["id"], "title": detail["title"], "newest": newest, "version": detail["version"]})
    json.dump(stale, sys.stdout, indent=2)
    sys.stdout.write("\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
