"""main.py 동작 검증 — 가짜 Ollama(mock_ollama.py)를 띄우고 FastAPI TestClient 로 엔드포인트를 호출한다.
실행(server 폴더에서): python tests/test_server.py [fast|slow|full]
  fast = 빠른 모드 + 압축 프롬프트(기본 설정), slow = 예전 호출 방식, full = 빠른 모드 + 예전 긴 프롬프트"""
import os
import sys
import json
import time
import tempfile
import threading

MODE = sys.argv[1] if len(sys.argv) > 1 else "fast"
PORT = 11499
os.environ["OLLAMA_URL"] = f"http://127.0.0.1:{PORT}"
os.environ["SEVENS_FAST"] = "0" if MODE == "slow" else "1"
os.environ["SEVENS_PROMPT"] = "full" if MODE == "full" else "compact"
FAST = MODE in ("fast", "full")
os.environ.setdefault("SEVENS_MODEL", "qwen3.5:4b")
os.environ.setdefault("SEVENS_JUDGE_MODEL", "exaone3.5:2.4b")

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(HERE, ".."))

import uvicorn
import mock_ollama as mock

server = uvicorn.Server(uvicorn.Config(mock.app, host="127.0.0.1", port=PORT, log_level="warning"))
threading.Thread(target=server.run, daemon=True).start()
while not server.started:
    time.sleep(0.05)

import main
from fastapi.testclient import TestClient

tmp = tempfile.NamedTemporaryFile("w", suffix=".json", delete=False)
tmp.write("{}")
tmp.close()
main.DATA_PATH = tmp.name

failures = []


def check(cond, msg):
    print(("  OK   " if cond else "  FAIL ") + msg)
    if not cond:
        failures.append(msg)


def calls_since(n):
    return mock.LOG[n:]


def wait_for(pred, timeout=15):
    t = time.time()
    while time.time() - t < timeout:
        if pred():
            return True
        time.sleep(0.1)
    return False


with TestClient(main.app) as c:
    print(f"== mode={MODE}  FAST_MODE={main.FAST_MODE}  PROMPT_STYLE={main.PROMPT_STYLE}")

    # 1) /conversation/exchange
    n = len(mock.LOG)
    t0 = time.time()
    r = c.post("/conversation/exchange", json={"char_a": "세비-호", "char_b": "류", "turns": 3}).json()
    ex_calls = calls_since(n)
    print(f"-- exchange: {len(ex_calls)} LLM calls, {time.time()-t0:.2f}s, mode={r.get('mode')}")
    check(len(r["lines"]) == 3, "exchange returns 3 lines")
    check([l["speaker"] for l in r["lines"]] == ["세비-호", "류", "세비-호"], "speakers alternate a,b,a")
    check(all(l["line"] for l in r["lines"]), "lines non-empty")
    check("situation" in r and "player_question" in r, "response keeps Unity fields")
    if FAST:
        check(len(ex_calls) == 1, "fast: exactly 1 LLM call for exchange")
        check(ex_calls[0].get("format") is not None, "fast: uses JSON schema format")
        check(ex_calls[0].get("think") is False, "qwen (thinking model) → think=false sent")
        check(ex_calls[0]["options"].get("num_ctx") == main.NUM_CTX, "num_ctx sent")
        rel = main.get_relationship("세비-호", "류")
        check(rel["intimacy"] == 2, f"relationship updated from delta (intimacy={rel['intimacy']})")
    else:
        check(len(ex_calls) >= 4, f"slow: legacy multi calls ({len(ex_calls)})")

    if MODE == "fast":
        sys_prompt = ex_calls[0]["messages"][0]["content"]
        check("정체:" in sys_prompt and "성격:" not in sys_prompt, "compact: exchange uses character cards")
        check("lines" in ex_calls[0]["format"]["properties"]
              and ex_calls[0]["format"]["properties"]["lines"]["items"] == {"type": "string"},
              "compact: lines are plain strings (no speaker objects)")
        check("situation" not in ex_calls[0]["format"]["properties"], "compact: exchange skips situation")

    # 2) /conversation/user : 대사 1회 + 백그라운드 판정 1회(나중에)
    n = len(mock.LOG)
    r = c.post("/conversation/user", json={"message": "안녕하세요", "target": "류"}).json()
    check(r["speaker"] == "류" and r["reply"], "user message reply")
    check("필요한 게 있으면" not in r["reply"] and not r["reply"].startswith("세비-호"),
          f"clean_line strips filler/label: {r['reply']!r}")
    check(len(calls_since(n)) == 1, "user: 1 synchronous call")
    if MODE == "fast":
        um = calls_since(n)[0]
        check(um["options"]["num_predict"] == 64, "compact: dialogue num_predict 64")
        check(len([m for m in um["messages"] if m["role"] != "system"]) <= 5, "compact: history ≤ 4 + new message")
    got = wait_for(lambda: any(b["model"] == main.JUDGE_MODEL for b in calls_since(n)))
    check(got, "background judge ran after response")
    judge = [b for b in calls_since(n) if b["model"] == main.JUDGE_MODEL]
    check(judge and "think" not in judge[0], "exaone (no thinking) → no think flag")
    check(judge and judge[0].get("stream") is True, "background call uses streaming (preemptible)")

    # 3) /game/result
    n = len(mock.LOG)
    r = c.post("/game/result", json={"winner": "플레이어", "player_rank": 1}).json()
    check(set(r["reactions"]) == set(main.DEFAULT_CHARACTERS), "reactions for all characters")
    if FAST:
        check(len(calls_since(n)) == 1, f"fast: game reactions in 1 call ({len(calls_since(n))})")

    # 4) /character/line + 미리 만들기
    time.sleep(2)
    n = len(mock.LOG)
    r = c.get("/character/line", params={"name": "진"}).json()
    first_calls = len(calls_since(n))
    expected = 1 if FAST else 2
    check(first_calls == expected, f"soliloquy sync calls = {first_calls} (expected {expected})")
    ok = wait_for(lambda: main._soliloquy_pool.get("진"), timeout=12)
    check(ok, "pregen filled pool for 진")
    n = len(mock.LOG)
    r2 = c.get("/character/line", params={"name": "진"}).json()
    check(r2["line"] and len([b for b in calls_since(n) if b.get("stream") is False]) == 0,
          "second /character/line served from pool (no sync call)")

    # 5) 선점: 백그라운드 생성 중 플레이어 요청 → 백그라운드 중단
    mock.STATE["stream_delay"] = 0.15
    with main._pool_lock:
        main._soliloquy_pool.clear()
    main._line_requested["세비-호"] = time.time()
    started = wait_for(lambda: any(b.get("stream") and "세비-호" in b["messages"][0]["content"]
                                   for b in mock.LOG[-3:]), timeout=10)
    check(started, "pregen for 세비-호 started (streaming)")
    t0 = time.time()
    r = c.post("/conversation/user", json={"message": "잠깐만요", "target": "세비-호"}).json()
    dt = time.time() - t0
    check(dt < 2.0, f"player request not blocked by background ({dt:.2f}s)")
    time.sleep(0.5)
    check(not main._soliloquy_pool.get("세비-호"), "preempted pregen did not store a line")
    mock.STATE["stream_delay"] = 0.0

    # 6) JSON 실패 → 예전 방식으로 대체
    if FAST:
        mock.STATE["bad_json"] = True
        n = len(mock.LOG)
        r = c.post("/conversation/exchange", json={"char_a": "진", "char_b": "류", "turns": 2}).json()
        check(r.get("mode") == "multi" and len(r["lines"]) == 2, "bad JSON → falls back to multi")
        mock.STATE["bad_json"] = False

    # 7) 상태
    s = c.get("/llm/status").json()
    check(s["model"] == main.MODEL_NAME and "calls" in s, "llm/status works")
    print("   status calls:", {k: v["calls"] for k, v in s["calls"].items()})

    # 8) 기타 엔드포인트 스모크
    for method, path, kw in [("get", "/", {}), ("get", "/conversation", {}), ("get", "/myroom/status", {}),
                             ("post", "/player/approach", {"params": {"name": "류"}}),
                             ("post", "/topic/add", {"json": {"text": "고양이"}}),
                             ("post", "/conversation/group", {"json": {"message": "다들 뭐해요?", "characters": ["류", "진"]}}),
                             ("post", "/characters/set", {"json": {"characters": ["류", "진"]}}),
                             ("post", "/conversation/next", {}),
                             ("post", "/event/trigger", {}),
                             ("get", "/conversation/status", {}), ("get", "/relationships", {})]:
        resp = getattr(c, method)(path, **kw)
        check(resp.status_code == 200, f"{method.upper()} {path} → {resp.status_code}")
    # 화제 강제 소비 확인
    r = c.post("/conversation/exchange", json={"char_a": "류", "char_b": "진", "turns": 3}).json()
    if FAST:
        last = mock.LOG[-1]
        check("고양이" in last["messages"][1]["content"], "forced topic injected into exchange prompt")

os.unlink(tmp.name)
print(f"\n{'ALL PASSED' if not failures else str(len(failures)) + ' FAILED'} ({MODE})")
sys.exit(1 if failures else 0)
