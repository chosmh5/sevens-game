"""Ollama API 흉내 서버 (테스트용). 요청을 기록하고, format(JSON 스키마)이 오면 스키마에 맞는 JSON을 돌려준다."""
import json
import time
import threading
from fastapi import FastAPI, Request
from fastapi.responses import StreamingResponse, JSONResponse

app = FastAPI()
LOG = []                 # 받은 /api/chat 요청들
LOCK = threading.Lock()
STATE = {"bad_json": False, "stream_delay": 0.0, "disconnects": 0, "gen_delay": 0.0}

LINES = ["어, 왔어요?", "오늘따라 좀 조용하네요.", "그럴 줄 알았어요.", "재밌는 선택이네요... 왜인지는 나중에 알게 되겠죠."]


def fake_from_schema(schema, i=0):
    t = schema.get("type")
    if t == "object":
        return {k: fake_from_schema(v, n) for n, (k, v) in enumerate(schema.get("properties", {}).items())}
    if t == "array":
        n = schema.get("minItems", 2)
        return [fake_from_schema(schema["items"], j) for j in range(n)]
    if t == "integer":
        return 2
    if "enum" in schema:
        return schema["enum"][i % len(schema["enum"])]
    return LINES[i % len(LINES)]


@app.post("/api/show")
async def show(req: Request):
    body = await req.json()
    m = body.get("model", "")
    caps = ["completion", "thinking"] if "qwen" in m else ["completion"]
    return {"capabilities": caps}


LOADED = set()


@app.get("/api/ps")
def ps():
    names = LOADED or {"mock:1b"}
    return {"models": [{"name": n, "model": n, "size": 2_000_000_000, "size_vram": 1_000_000_000,
                        "context_length": 4096} for n in names]}


@app.get("/api/tags")
def tags():
    return {"models": [{"name": n, "model": n, "size": 1} for n in ["exaone3.5:7.8b", "exaone3.5:2.4b", "qwen3.5:4b"]]}


@app.get("/api/version")
def version():
    return {"version": "0.99.0-mock"}


@app.post("/api/generate")
async def generate(req: Request):
    body = await req.json()
    if body.get("keep_alive") == 0:
        LOADED.discard(body["model"])
    else:
        LOADED.add(body["model"])
    return {"done": True, "load_duration": 1_000_000}


@app.post("/api/chat")
async def chat(req: Request):
    body = await req.json()
    with LOCK:
        LOG.append(body)
    fmt = body.get("format")
    if fmt is not None:
        content = "not json at all" if STATE["bad_json"] else json.dumps(fake_from_schema(fmt), ensure_ascii=False)
    else:
        content = "세비-호: \"" + LINES[len(LOG) % len(LINES)] + " 필요한 게 있으면 말씀하세요.\""
    stats = {"total_duration": 5e8, "load_duration": 1e6, "prompt_eval_count": 300,
             "prompt_eval_duration": 3e8, "eval_count": 20, "eval_duration": 2e8}
    if STATE["gen_delay"]:
        time.sleep(STATE["gen_delay"])
    if not body.get("stream"):
        return JSONResponse({"model": body["model"], "message": {"role": "assistant", "content": content},
                             "done": True, **stats})

    delay = STATE["stream_delay"]

    def gen():
        try:
            for ch in content:
                time.sleep(delay)
                yield json.dumps({"message": {"role": "assistant", "content": ch}, "done": False}) + "\n"
            yield json.dumps({"message": {"role": "assistant", "content": ""}, "done": True, **stats}) + "\n"
        except GeneratorExit:
            STATE["disconnects"] += 1
            raise
    return StreamingResponse(gen(), media_type="application/x-ndjson")
