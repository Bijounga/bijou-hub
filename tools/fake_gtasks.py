"""A small in-memory stand-in for the Google Tasks REST API, for testing BijouHub's sync.

    python tools/fake_gtasks.py 47950 seed.json

Run BijouHub with
    BIJOUHUB_GTASKS_BASE=http://127.0.0.1:47950/tasks/v1
    BIJOUHUB_GTASKS_TEST_TOKEN=test-token
(and BIJOUHUB_DATA_DIR pointing at scratch data).

Implements the endpoints BijouHub calls, with the API's filtering rules (showCompleted,
showHidden, completedMin), plus test-only helpers:
    GET  /_state               every list and task, including deleted ones
    POST /_phone               {"list": "<title>", "title": "...", "status": "completed"?}
                               adds a task as if from the phone app
    POST /_patch               {"list": "<title>", "title": "<current>", "set": {...}} edits one
"""
import json
import sys
import uuid
from datetime import datetime, timezone
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs, unquote, urlparse

TOKEN = "Bearer test-token"
LISTS = {}   # id -> {"id", "title"}
TASKS = {}   # list id -> [task dict]
LOG = []


def now():
    return datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%S.000Z")


def new_list(title):
    lid = "L" + uuid.uuid4().hex[:8]
    LISTS[lid] = {"id": lid, "title": title, "kind": "tasks#taskList"}
    TASKS[lid] = []
    return LISTS[lid]


def new_task(lid, title, notes=None, status="needsAction", completed=None):
    task = {"id": "T" + uuid.uuid4().hex[:10], "title": title, "status": status, "updated": now(),
            "position": str(len(TASKS[lid])).zfill(20), "kind": "tasks#task"}
    if notes:
        task["notes"] = notes
    if status == "completed":
        task["completed"] = completed or now()
        task["hidden"] = False
    TASKS[lid].append(task)
    return task


def list_by_title(title):
    return next(lid for lid, l in LISTS.items() if l["title"] == title)


class Handler(BaseHTTPRequestHandler):
    # Persistent HTTP/1.1 like the real API; HTTP/1.0's close-per-request trips up .NET's
    # pooled parallel connections on Windows (WSAECONNABORTED).
    protocol_version = "HTTP/1.1"

    def log_message(self, *args):
        pass

    def send_json(self, code, body=None):
        data = b"" if body is None else json.dumps(body).encode()
        self.send_response(code)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def body(self):
        length = int(self.headers.get("Content-Length") or 0)
        return json.loads(self.rfile.read(length) or b"{}") if length else {}

    def route(self, method):
        url = urlparse(self.path)
        parts = [unquote(p) for p in url.path.strip("/").split("/")]
        query = {k: v[0] for k, v in parse_qs(url.query).items()}

        if parts[0] == "_state":
            return self.send_json(200, {"lists": list(LISTS.values()), "tasks": TASKS, "log": LOG})
        if parts[0] == "_phone":
            b = self.body()
            return self.send_json(200, new_task(list_by_title(b["list"]), b["title"], status=b.get("status", "needsAction")))
        if parts[0] == "_patch":
            b = self.body()
            task = next(t for t in TASKS[list_by_title(b["list"])] if t["title"] == b["title"])
            task.update(b["set"])
            return self.send_json(200, task)

        if self.headers.get("Authorization") != TOKEN:
            return self.send_json(401, {"error": {"code": 401, "message": "Invalid credentials"}})
        LOG.append(f"{method} {url.path}{'?' + url.query if url.query else ''}")

        # /tasks/v1/users/@me/lists
        if parts[:4] == ["tasks", "v1", "users", "@me"] and parts[4:] == ["lists"]:
            if method == "GET":
                return self.send_json(200, {"kind": "tasks#taskLists", "items": list(LISTS.values())})
            if method == "POST":
                return self.send_json(200, new_list(self.body()["title"]))

        # /tasks/v1/lists/{list}/tasks[/{task}]
        if parts[:3] == ["tasks", "v1", "lists"] and len(parts) >= 5 and parts[4] == "tasks":
            lid = parts[3]
            if lid not in LISTS:
                return self.send_json(404, {"error": {"code": 404, "message": "Task list not found"}})
            if len(parts) == 5:
                if method == "GET":
                    show_completed = query.get("showCompleted", "true") == "true"
                    show_hidden = query.get("showHidden", "false") == "true"
                    completed_min = query.get("completedMin")
                    items = []
                    for t in TASKS[lid]:
                        if t.get("deleted"):
                            continue
                        done = t["status"] == "completed"
                        if done and not show_completed:
                            continue
                        if t.get("hidden") and not show_hidden:
                            continue
                        if completed_min and (not done or t["completed"] < completed_min):
                            continue
                        items.append(t)
                    return self.send_json(200, {"kind": "tasks#tasks", "items": items})
                if method == "POST":
                    b = self.body()
                    return self.send_json(200, new_task(lid, b.get("title", ""), b.get("notes"), b.get("status", "needsAction")))
            if len(parts) == 6:
                task = next((t for t in TASKS[lid] if t["id"] == parts[5] and not t.get("deleted")), None)
                if task is None:
                    return self.send_json(404, {"error": {"code": 404, "message": "Task not found"}})
                if method == "PATCH":
                    b = self.body()
                    for key, value in b.items():
                        if value is None:
                            task.pop(key, None)
                        else:
                            task[key] = value
                    if b.get("status") == "completed" and "completed" not in task:
                        task["completed"] = now()
                    task["updated"] = now()
                    return self.send_json(200, task)
                if method == "DELETE":
                    task["deleted"] = True
                    return self.send_json(204)

        return self.send_json(404, {"error": {"code": 404, "message": "No such endpoint"}})

    def do_GET(self):
        self.route("GET")

    def do_POST(self):
        self.route("POST")

    def do_PATCH(self):
        self.route("PATCH")

    def do_DELETE(self):
        self.route("DELETE")


if __name__ == "__main__":
    port = int(sys.argv[1])
    if len(sys.argv) > 2:
        seed = json.load(open(sys.argv[2], encoding="utf-8-sig"))
        for title, tasks in seed.items():
            lid = new_list(title)["id"]
            for t in tasks:
                new_task(lid, t["title"], status=t.get("status", "needsAction"), completed=t.get("completed"))
    ThreadingHTTPServer.request_queue_size = 64  # the default backlog of 5 drops bursts of parallel requests
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
