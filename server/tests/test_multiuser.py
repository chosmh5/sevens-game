"""여러 사용자(X-User-Id)가 한 서버를 같이 써도 대화·기억·관계가 섞이지 않는지 검증.
실행(server 폴더에서): python tests/test_multiuser.py"""
import os
import sys
import json
import time
import tempfile
import threading

PORT = 11498
os.environ["OLLAMA_URL"] = f"http://127.0.0.1:{PORT}"
os.environ["SEVENS_FAST"] = "1"
os.environ["SEVENS_PROMPT"] = "compact"
os.environ.setdefault("SEVENS_MODEL", "qwen3.5:4b")

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
main.USERS_DIR = tempfile.mkdtemp(prefix="sevens-users-")

failures = []


def check(cond, msg):
    print(("  OK   " if cond else "  FAIL ") + msg)
    if not cond:
        failures.append(msg)


def wait_for(pred, timeout=15):
    t = time.time()
    while time.time() - t < timeout:
        if pred():
            return True
        time.sleep(0.1)
    return False


def H(uid):
    return {"X-User-Id": uid}


def user_file(uid):
    return os.path.join(main.USERS_DIR, f"{uid}.json")


def read_user(uid):
    with open(user_file(uid), encoding="utf-8") as f:
        return json.load(f)


with TestClient(main.app) as c:
    # 0) 사용자 ID 인식
    check(c.get("/", headers=H("alice")).json()["user"] == "alice", "header X-User-Id → user")
    check(c.get("/", params={"user_id": "bob"}).json()["user"] == "bob", "query ?user_id= → user (header 없을 때)")
    check(c.get("/").json()["user"] == "default", "no id → default")
    check(c.get("/", headers=H("../../etc/passwd")).json()["user"] == "etcpasswd", "unsafe id sanitized")
    check(main.normalize_user_id("한글만") == "default", "non-ascii-only id → default")
    check(c.get("/", headers=H("AbC123")).json()["user"] == "abc123", "id lowercased (Windows 파일명 대소문자 무시)")

    # 1) 처음 보는 사용자는 기본 기억(seed_memory.json)을 각자 받는다
    seed_names = [n for n in json.load(open(main.SEED_MEMORY_PATH, encoding="utf-8")) if not n.startswith("_")]
    m_a = c.get(f"/memory/{seed_names[0]}", headers=H("alice")).json()
    m_b = c.get(f"/memory/{seed_names[0]}", headers=H("bob")).json()
    check(m_a["notes"] and m_a["notes"] == m_b["notes"], "new users both get seed memory")
    check(os.path.exists(user_file("alice")) and os.path.exists(user_file("bob")), "per-user files created")

    # 2) 대화 기록 분리
    c.post("/conversation/user", headers=H("alice"), json={"message": "앨리스의 비밀 이야기", "target": "세비-호"})
    a_msgs = [m["message"] for m in c.get("/conversation", params={"limit": 20}, headers=H("alice")).json()["messages"]]
    b_msgs = [m["message"] for m in c.get("/conversation", params={"limit": 20}, headers=H("bob")).json()["messages"]]
    check("앨리스의 비밀 이야기" in a_msgs, "alice sees her own message")
    check("앨리스의 비밀 이야기" not in b_msgs, "bob does NOT see alice's message")

    # 3) 기억(data 파일) 분리
    a_log = [l["player"] for l in c.get("/memory/세비-호", headers=H("alice")).json()["recent"]]
    b_log = [l["player"] for l in c.get("/memory/세비-호", headers=H("bob")).json()["recent"]]
    check("앨리스의 비밀 이야기" in a_log, "alice's memory log has her line")
    check("앨리스의 비밀 이야기" not in b_log, "bob's memory log does not")
    with open(main.DATA_PATH, encoding="utf-8") as f:
        check("앨리스의 비밀 이야기" not in f.read(), "default data.json untouched")

    # 4) 백그라운드 작업(관계 판정·기억 추출)도 요청한 사용자 파일에 기록
    before_b = read_user("bob").get("relationships", {}).get(main._rel_key("플레이어", "세비-호"))
    ok = wait_for(lambda: main._rel_key("플레이어", "세비-호") in read_user("alice").get("relationships", {})
                  and main._bg_queue.qsize() == 0, timeout=20)
    time.sleep(0.5)
    check(ok, "background judge wrote to alice's file")
    check(read_user("bob").get("relationships", {}).get(main._rel_key("플레이어", "세비-호")) == before_b,
          "background judge did not touch bob's file")

    # 5) 게임 결과 → 그 사용자 관계만 변화
    c.post("/game/result", headers=H("bob"), json={"winner": "플레이어", "player_rank": 1})
    rb = c.get("/myroom/status", headers=H("bob")).json()["player_relations"]["류"]["intimacy"]
    ra = c.get("/myroom/status", headers=H("alice")).json()["player_relations"]["류"]["intimacy"]
    check(rb > 0 and ra == 0, f"game result only changes bob's relations (bob={rb}, alice={ra})")

    # 6) 화제·활성 캐릭터·초기화 분리
    c.post("/topic/add", headers=H("alice"), json={"text": "앨리스 화제"})
    check(main._st("alice").pending_topic == "앨리스 화제" and main._st("bob").pending_topic is None,
          "pending topic per user")
    c.post("/characters/set", headers=H("bob"), json={"characters": ["진", "류"]})
    check(main._st("bob").active == ["진", "류"] and main._st("alice").active == ["세비-호"], "active characters per user")
    c.post("/conversation/clear", headers=H("bob"))
    a_after = [m["message"] for m in c.get("/conversation", params={"limit": 20}, headers=H("alice")).json()["messages"]]
    check("앨리스의 비밀 이야기" in a_after, "bob's /conversation/clear does not clear alice")

    # 7) 반응 대사 묶음 예약도 사용자별
    r = c.get("/game/reaction_bank", headers=H("carol")).json()
    check(set(r["pending"]) == set(main.DEFAULT_CHARACTERS), "carol's reaction bank scheduled for her")
    ok = wait_for(lambda: len(c.get("/game/reaction_bank", headers=H("carol")).json()["lines"]) > 0, timeout=25)
    check(ok and "reaction_bank" in read_user("carol"), "carol's reaction bank saved in carol's file")

    # 8) 동시 사용자 3명 — 서버가 한 명씩 줄 세우지 않는지 + 응답이 섞이지 않는지
    wait_for(lambda: main._bg_queue.qsize() == 0, timeout=20)
    mock.STATE["gen_delay"] = 1.0
    results = {}

    def talk(uid):
        t0 = time.time()
        r = c.post("/conversation/user", headers=H(uid), json={"message": f"{uid} 동시 메시지", "target": "진"})
        results[uid] = (r.status_code, time.time() - t0)

    users = ["u1", "u2", "u3"]
    t0 = time.time()
    ths = [threading.Thread(target=talk, args=(u,)) for u in users]
    for t in ths:
        t.start()
    for t in ths:
        t.join()
    total = time.time() - t0
    mock.STATE["gen_delay"] = 0.0
    check(all(results[u][0] == 200 for u in users), "3 concurrent users all 200")
    check(total < 2.5, f"3 concurrent requests handled in parallel ({total:.2f}s, LLM 1.0s each)")
    for u in users:
        msgs = [m["message"] for m in c.get("/conversation", params={"limit": 20}, headers=H(u)).json()["messages"]]
        others = [o for o in users if o != u]
        check(f"{u} 동시 메시지" in msgs and not any(f"{o} 동시 메시지" in msgs for o in others),
              f"{u}: only own message in history")

    # 9) 상태 확인
    s = c.get("/llm/status", headers=H("alice")).json()
    check(s["user"] == "alice" and {"alice", "bob"} <= set(s["users_in_memory"]), "llm/status shows users")

print()
print("ALL PASSED (multiuser)" if not failures else f"{len(failures)} FAILED (multiuser)")
sys.exit(1 if failures else 0)
