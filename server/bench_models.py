"""
SEVENS 캐릭터 대사 모델 벤치마크
================================
서버(main.py)와 똑같은 프롬프트로 후보 모델을 돌려서
  - 속도: 모델 로드, 대사 1줄, NPC 대화 묶음(빠른 모드 1회 호출 vs 예전 방식) 시간
  - 규칙 준수: 존댓말(~요/~죠), 중국어·일본어 혼입, 번역투(~니다), 길이, 비서 말투
  - 구조화 출력: NPC 대화·게임 반응 JSON 성공률, 관계 판정 형식
을 재고 bench_results/<시각>/report.html 로 정리한다.

사용법 (server 폴더에서. uvicorn 서버는 꺼 두는 게 정확하다)
  python bench_models.py                  설치된 후보만 측정(없는 모델은 받는 명령을 알려줌)
  python bench_models.py --pull           없는 후보를 받아서 측정
  python bench_models.py --models qwen3.5:4b,exaone3.5:2.4b
  python bench_models.py --quick          빠르게(반복 1회, 케이스 일부)
  python bench_models.py --cpu-compare    GPU(Vulkan) 사용 시 CPU 전용과 속도 비교
  python bench_models.py --prompt both    같은 모델로 압축 프롬프트 vs 예전 긴 프롬프트 비교
"""
import argparse
import csv
import html
import json
import os
import platform
import re
import statistics
import sys
import time
from datetime import datetime

import requests

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import main as srv  # noqa: E402  (서버의 프롬프트·후처리를 그대로 사용)

OLLAMA = srv.OLLAMA_URL

# 후보 모델 (2026-09 기준). 필요하면 --models 로 바꾼다.
CANDIDATES = [
    ("exaone3.5:7.8b", "현재 대사 모델 (기준)"),
    ("exaone3.5:2.4b", "현재 판정 모델 (기준)"),
    ("qwen3.5:4b", "Qwen 3.5 4B · 2026"),
    ("qwen3.5:2b", "Qwen 3.5 2B · 2026"),
    ("gemma4:e2b-it-qat", "Gemma 4 E2B (QAT) · 2026"),
    ("hf.co/LGAI-EXAONE/EXAONE-4.0-1.2B-GGUF:Q4_K_M", "EXAONE 4.0 1.2B · LG"),
    ("hf.co/Grit-Labs/kanana-1.5-2.1b-instruct-2505-GGUF:Q4_K_M", "Kanana 1.5 2.1B · 카카오"),
]
OPTIONAL = [
    ("gemma4:e4b-it-qat", "Gemma 4 E4B (QAT) · 2026, 6GB"),
    ("qwen3.5:9b", "Qwen 3.5 9B · 2026, 6.6GB"),
]

# ── 고정 테스트 맥락 (data.json 은 건드리지 않는다) ─────────────────
_REL = {
    "awkward": ({"intimacy": 0, "trust": 0, "emotion": "neutral", "milestones": []},
                "현재 관계: 처음 만난 어색한 사이 (친밀도 +0, 신뢰 0/100, 감정: neutral)"),
    "friendly": ({"intimacy": 25, "trust": 30, "emotion": "friendly", "milestones": [{"text": "카드게임 역전승"}]},
                 "현재 관계: 조금 친해진 사이 (친밀도 +25, 신뢰 30/100, 감정: friendly)\n최근 있었던 일: 카드게임 역전승"),
    "tense": ({"intimacy": -30, "trust": 10, "emotion": "tense", "milestones": [{"text": "카드게임 내기 다툼"}]},
              "현재 관계: 사이가 껄끄러운 사이 (친밀도 -30, 신뢰 10/100, 감정: tense)\n최근 있었던 일: 카드게임 내기 다툼"),
}


def rel(kind):
    """서버와 같은 형식의 관계 문구(압축 모드면 한 줄)."""
    d, full = _REL[kind]
    return srv.compact_relation(d) if srv.PROMPT_STYLE == "compact" else full


def compact():
    return srv.PROMPT_STYLE == "compact"


PLAYER = "플레이어(유저)"


def H(speaker, message):
    return {"speaker": speaker, "message": message}


def dialogue_cases():
    """(id, 설명, 화자, messages) — 서버의 build_character_messages 로 만든다."""
    m = srv.build_character_messages
    return [
        ("approach", "다가갔을 때 첫마디", "세비-호",
         m("세비-호", [], srv.approach_situation("세비-호"), None, talk_target=PLAYER)),
        ("chat", "플레이어 말에 대답", "류",
         m("류", [H("플레이어", "오늘 카드게임 한 판 어때요? 이번엔 제가 이길 것 같은데요.")], None,
           rel("awkward"), talk_target=PLAYER)),
        ("topic", "플레이어가 꺼낸 화제", "진",
         m("진", [H("플레이어", "요즘 고양이를 키워볼까 고민 중이에요.")], None, rel("friendly"), talk_target=PLAYER)),
        ("banmal_bait", "반말로 말 걸기(존댓말 유지?)", "세비-호",
         m("세비-호", [H("플레이어", "야, 너 오늘 좀 이상하다?")], None, rel("awkward"), talk_target=PLAYER)),
        ("long_bait", "긴 설명 유도(짧게 답하나?)", "류",
         m("류", [H("플레이어", "세븐 카드게임 규칙 좀 자세히 설명해 줄래요?")], None, rel("awkward"), talk_target=PLAYER)),
        ("npc_start", "NPC끼리 대화 시작", "세비-호",
         m("세비-호", [], "[상황] 어제 카드게임에서 세비-호가 류에게 졌고, 오늘 둘이 광장에서 마주쳤다.",
           rel("tense"), talk_target="류")),
        ("npc_reply", "NPC끼리 대화 이어가기", "류",
         m("류", [H("세비-호", "어제는 운이 없었던 거예요. 실력은 제가 위죠.")], None, rel("tense"), talk_target="세비-호")),
        ("soliloquy", "혼잣말(빠른 모드)", "진",
         m("진", [], srv.soliloquy_situation("진", "", seed="요즘 카드게임"), None, talk_target=PLAYER)),
    ]


QUICK_CASES = {"approach", "chat", "banmal_bait", "npc_reply"}

# ── Ollama 호출 ──────────────────────────────────────────────


def api(method, path, timeout=30, **kw):
    r = requests.request(method, f"{OLLAMA}{path}", timeout=timeout, **kw)
    r.raise_for_status()
    return r.json()


def chat(model, messages, temperature, num_predict, fmt=None, extra_opts=None):
    """서버와 같은 옵션으로 호출하고 (content, 통계) 반환."""
    payload = {"model": model, "messages": messages, "stream": False, "keep_alive": "30m",
               "options": srv._model_options(model, temperature, num_predict)}
    if extra_opts:
        payload["options"].update(extra_opts)
    if "thinking" in srv._model_caps(model):
        payload["think"] = False
    if fmt is not None:
        payload["format"] = fmt
    t0 = time.time()
    data = requests.post(f"{OLLAMA}/api/chat", json=payload, timeout=600).json()
    wall = time.time() - t0
    if "error" in data:
        raise RuntimeError(data["error"])
    content = data.get("message", {}).get("content", "")
    ns = 1e9
    pe_n, pe_d = data.get("prompt_eval_count", 0), data.get("prompt_eval_duration", 0) / ns
    ev_n, ev_d = data.get("eval_count", 0), data.get("eval_duration", 0) / ns
    stats = {
        "wall_s": round(wall, 2),
        "load_s": round(data.get("load_duration", 0) / ns, 2),
        "prompt_tokens": pe_n,
        "prefill_tps": round(pe_n / pe_d, 1) if pe_d > 0 else None,
        "gen_tokens": ev_n,
        "gen_tps": round(ev_n / ev_d, 1) if ev_d > 0 else None,
    }
    return srv._THINK_RE.sub("", content).strip(), stats, content


def unload_all():
    try:
        for m in api("GET", "/api/ps").get("models", []):
            requests.post(f"{OLLAMA}/api/generate", json={"model": m["name"], "keep_alive": 0}, timeout=60)
        for _ in range(30):
            if not api("GET", "/api/ps").get("models"):
                return
            time.sleep(0.5)
    except Exception as e:
        print(f"   (언로드 확인 실패: {e})")


def load(model, extra_opts=None):
    t0 = time.time()
    body = {"model": model, "keep_alive": "30m"}
    if extra_opts:
        body["options"] = extra_opts
    r = requests.post(f"{OLLAMA}/api/generate", json=body, timeout=900).json()
    if "error" in r:
        raise RuntimeError(r["error"])
    return round(time.time() - t0, 1)


def ps_info(model):
    for m in api("GET", "/api/ps").get("models", []):
        if m.get("name") == model or m.get("model") == model:
            size = m.get("size", 0)
            return {"size_gb": round(size / 1e9, 2),
                    "gpu_pct": round(100 * m.get("size_vram", 0) / size) if size else 0,
                    "ctx": m.get("context_length")}
    return {"size_gb": None, "gpu_pct": None, "ctx": None}


def pull(model):
    print(f"   받는 중: {model}")
    last = 0
    with requests.post(f"{OLLAMA}/api/pull", json={"model": model, "stream": True}, stream=True, timeout=None) as r:
        for raw in r.iter_lines():
            if not raw:
                continue
            j = json.loads(raw)
            if "error" in j:
                raise RuntimeError(j["error"])
            if j.get("total") and time.time() - last > 3:
                last = time.time()
                print(f"     {j.get('status', '')} {100 * j.get('completed', 0) / j['total']:.0f}%")
    print("   받기 완료")


# ── 규칙 검사 ────────────────────────────────────────────────
HAN = re.compile(r"[一-鿿]")
KANA = re.compile(r"[぀-ヿ]")
LATIN = re.compile(r"[A-Za-z]{3,}")
HANGUL = re.compile(r"[가-힣]")
SENT_SPLIT = re.compile(r"[.!?。…~]+|\n")
VOCATIVE = re.compile(r"[,\s]+(플레이어|세비-호|류|진)(님|씨)?\s*$")


def sentences(text):
    return [s.strip() for s in SENT_SPLIT.split(text or "") if HANGUL.search(s or "")]


def check_line(raw, cleaned):
    ends = []
    for s in sentences(cleaned):
        s = VOCATIVE.sub("", s)   # "알겠어요, 플레이어" 처럼 끝에 붙은 호칭은 빼고 본다
        hs = HANGUL.findall(s)
        if len(hs) <= 2:          # "흠", "아" 같은 감탄사 문장은 제외
            continue
        ends.append(hs[-1])
    polite = bool(ends) and all(e in ("요", "죠") for e in ends)
    formal = bool(re.search(r"니다(?=[\s.!?~…]|$)", cleaned or ""))
    letters = len(HANGUL.findall(cleaned or "")) + len(re.findall(r"[A-Za-z一-鿿぀-ヿ]", cleaned or ""))
    res = {
        "empty": not cleaned,
        "han": bool(HAN.search(cleaned or "")),
        "kana": bool(KANA.search(cleaned or "")),
        "latin": bool(LATIN.search(cleaned or "")),
        "hangul_ratio": round(len(HANGUL.findall(cleaned or "")) / letters, 2) if letters else 0,
        "polite": polite,
        "formal": formal,
        "raw_sentences": len(sentences(raw)),
        "filler": any(p in (raw or "") for p in srv._FILLER_PATTERNS),
        "think_leak": "<think" in (raw or "") or "</think>" in (raw or ""),
        "length": len(cleaned or ""),
    }
    res["ok"] = (not res["empty"] and not res["han"] and not res["kana"] and res["polite"]
                 and not res["formal"] and res["length"] <= 80)
    return res


# ── 측정 ─────────────────────────────────────────────────────


STYLE_KO = {"compact": "압축 프롬프트", "full": "예전 프롬프트"}


def bench_model(model, reps, quick, cpu_compare, styles=("compact",)):
    """모델을 한 번 올려 두고 프롬프트 방식(styles)마다 같은 케이스를 잰다. (요약 목록, 행 목록) 반환."""
    print(f"\n▶ {model}")
    unload_all()
    try:
        load_s = load(model)
    except Exception as e:
        print(f"   로드 실패: {e}")
        return [{"model": model, "model_name": model, "style": None, "error": str(e)}], []
    info = ps_info(model)
    caps = sorted(srv._model_caps(model))
    print(f"   로드 {load_s}s · 메모리 {info['size_gb']}GB · GPU {info['gpu_pct']}% · 기능 {caps}")
    summaries, all_rows = [], []
    for i, style in enumerate(styles):
        srv.PROMPT_STYLE = style
        label = model if len(styles) == 1 else f"{model} · {STYLE_KO[style]}"
        if len(styles) > 1:
            print(f"   ── {STYLE_KO[style]}")
        s, rows = _bench_style(model, label, reps, quick, cpu_compare and i == 0, load_s, info, caps)
        s["model_name"], s["style"] = model, style
        summaries.append(s)
        all_rows += rows
    unload_all()
    return summaries, all_rows


def _bench_style(model, label, reps, quick, cpu_compare, load_s, info, caps):
    rows = []

    # 1) 대사
    for cid, desc, speaker, messages in dialogue_cases():
        if quick and cid not in QUICK_CASES:
            continue
        for rep in range(reps):
            try:
                out, st, raw = chat(model, messages, 0.85, 64 if compact() else 128)
                cleaned = srv.clean_line(out)
                chk = check_line(out, cleaned)
            except Exception as e:
                cleaned, st, chk, raw = f"(오류: {e})", {"wall_s": None}, {"ok": False}, ""
            rows.append({"model": label, "case": cid, "desc": desc, "speaker": speaker, "rep": rep,
                         "kind": "dialogue", "text": cleaned, "raw": raw, **st, **chk})
            print(f"   [{cid}] {st.get('wall_s')}s {'✓' if chk.get('ok') else '✗'} {cleaned[:50]}")

    # 2) NPC 대화 묶음 — 빠른 모드(JSON 1회)
    for rep in range(reps if not quick else 1):
        msgs, schema = srv.build_exchange_messages(
            "세비-호", "진", 3, rel("friendly"), "최근 있었던 일: 카드게임 역전승",
            "요즘 플레이어가 꺼낸 화제: 고양이", "", None, asker="진", seed="고양이")
        try:
            out, st, raw = chat(model, msgs, 0.85, (50 + 45 * 3 + 45) if compact() else (160 + 90 * 3), fmt=schema)
            parsed = srv.parse_exchange(out, "세비-호", "진", 3)
        except Exception as e:
            out, st, raw, parsed = f"(오류: {e})", {"wall_s": None}, "", None
        ok_json = parsed is not None and len(parsed["lines"]) == 3
        lines = parsed["lines"] if parsed else []
        line_checks = [check_line(l["line"], l["line"]) for l in lines]
        rows.append({"model": label, "case": "exchange_fast", "desc": "NPC 대화 묶음 · 빠른 모드(1회)",
                     "speaker": "세비-호/진", "rep": rep, "kind": "exchange",
                     "text": " / ".join(f"{l['speaker']}: {l['line']}" for l in lines) +
                             (f"  → 플레이어에게: {parsed['player_question']}" if parsed and parsed.get("player_question") else "")
                             if parsed else out[:200],
                     "raw": raw, **st, "json_ok": ok_json,
                     "ok": ok_json and all(c["ok"] for c in line_checks),
                     "polite": all(c["polite"] for c in line_checks) if line_checks else False,
                     "han": any(c["han"] for c in line_checks), "kana": any(c["kana"] for c in line_checks),
                     "length": round(statistics.mean(c["length"] for c in line_checks)) if line_checks else 0})
        print(f"   [exchange_fast] {st.get('wall_s')}s JSON {'✓' if ok_json else '✗'}")

    # 3) NPC 대화 묶음 — 예전 방식(상황 1회 + 대사 3회) 체감 시간
    t0 = time.time()
    try:
        sit_prompt = srv.build_situation_prompt("세비-호", "진", rel("friendly"), "최근 있었던 일: 카드게임 역전승",
                                                "요즘 플레이어가 꺼낸 화제: 고양이")
        situation, _, _ = chat(model, [{"role": "user", "content": sit_prompt}], 0.3, 60)
        hist = []
        texts = []
        for i, sp in enumerate(["세비-호", "진", "세비-호"]):
            other = "진" if sp == "세비-호" else "세비-호"
            um = f"[상황] {situation}" if i == 0 else None
            line, _, _ = chat(model, srv.build_character_messages(sp, hist, um, rel("friendly"), talk_target=other), 0.85,
                              64 if compact() else 128)
            line = srv.clean_line(line)
            hist.append(H(sp, line))
            texts.append(f"{sp}: {line}")
        legacy_wall = round(time.time() - t0, 2)
        legacy_text = " / ".join(texts)
    except Exception as e:
        legacy_wall, legacy_text = None, f"(오류: {e})"
    rows.append({"model": label, "case": "exchange_legacy", "desc": "NPC 대화 묶음 · 예전 방식(4회)",
                 "speaker": "세비-호/진", "rep": 0, "kind": "legacy", "text": legacy_text, "raw": "",
                 "wall_s": legacy_wall, "ok": None})
    print(f"   [exchange_legacy] {legacy_wall}s")

    # 4) 게임 결과 반응 (JSON 1회)
    chars = list(srv.DEFAULT_CHARACTERS)
    msgs, schema = srv.build_reaction_messages(chars, 1, {c: {"intimacy": 10, "trust": 20} for c in chars})
    try:
        out, st, raw = chat(model, msgs, 0.85, (45 if compact() else 80) * len(chars), fmt=schema)
        parsed = json.loads(out)
        reacts = {c: srv.clean_line(str(parsed.get(c, ""))) for c in chars}
        ok_json = all(reacts.values())
        rchecks = [check_line(v, v) for v in reacts.values()]
    except Exception as e:
        st, reacts, ok_json, rchecks, raw = {"wall_s": None}, {}, False, [], str(e)
    rows.append({"model": label, "case": "reactions", "desc": "게임 결과 반응 · 3명(1회)", "speaker": "전원",
                 "rep": 0, "kind": "reactions", "text": " / ".join(f"{k}: {v}" for k, v in reacts.items()),
                 "raw": raw, **st, "json_ok": ok_json, "ok": ok_json and all(c["ok"] for c in rchecks),
                 "polite": all(c["polite"] for c in rchecks) if rchecks else False})
    print(f"   [reactions] {st.get('wall_s')}s JSON {'✓' if ok_json else '✗'}")

    # 5) 관계 판정 형식 (판정 모델로 쓸 수 있나)
    judge_ok = 0
    jr = 1 if quick else 2
    for rep in range(jr):
        prompt = ("다음은 '플레이어'와(과) '류'의 대화야. 이 대화로 둘 사이가 어떻게 변했는지 판단해.\n\n"
                  "[대화]\n플레이어: 어제 도와줘서 고마웠어요.\n류: 별거 아니었어요.\n\n"
                  "친밀도 변화량을 -3~+3 정수로 정해(따뜻·친근하면 +, 평범·무관심하면 0, 차갑거나 다투면 -). "
                  "감정은 friendly/neutral/curious/tense/hostile 중 하나.\n"
                  "반드시 이 한 줄 형식으로만 답해: delta=<정수> emotion=<단어>")
        try:
            out, st, raw = chat(model, [{"role": "user", "content": prompt}], 0.3, 60)
            ok = bool(re.search(r"delta\s*=\s*-?\d+", out)) and bool(re.search(r"emotion\s*=\s*[a-zA-Z]+", out))
        except Exception as e:
            out, st, ok = f"(오류: {e})", {"wall_s": None}, False
        judge_ok += ok
        rows.append({"model": label, "case": "judge", "desc": "관계 판정 형식", "speaker": "-", "rep": rep,
                     "kind": "judge", "text": out[:80], "raw": out, **st, "ok": ok})

    # 6) CPU 전용 비교
    cpu = None
    if cpu_compare and (info.get("gpu_pct") or 0) > 0:
        try:
            unload_all()
            load(model, {"num_gpu": 0})
            tps = []
            for _ in range(2):
                _, st, _ = chat(model, dialogue_cases()[1][3], 0.85, 64 if compact() else 128, extra_opts={"num_gpu": 0})
                tps.append(st)
            cpu = {"wall_s": statistics.median(s["wall_s"] for s in tps),
                   "gen_tps": statistics.median(s["gen_tps"] or 0 for s in tps),
                   "prefill_tps": statistics.median(s["prefill_tps"] or 0 for s in tps)}
            print(f"   [CPU 전용] 대사 {cpu['wall_s']}s · 생성 {cpu['gen_tps']} tok/s")
        except Exception as e:
            print(f"   CPU 비교 실패: {e}")

    return summarize(label, rows, load_s, info, caps, judge_ok / jr, cpu), rows


def _med(xs):
    xs = [x for x in xs if x is not None]
    return round(statistics.median(xs), 1) if xs else None


def summarize(model, rows, load_s, info, caps, judge_rate, cpu):
    dl = [r for r in rows if r["kind"] == "dialogue" and r.get("wall_s") is not None]
    ex = [r for r in rows if r["kind"] == "exchange"]
    lg = [r for r in rows if r["kind"] == "legacy" and r.get("wall_s") is not None]
    rc = [r for r in rows if r["kind"] == "reactions"]

    def rate(xs, key):
        return round(100 * sum(1 for x in xs if x.get(key)) / len(xs)) if xs else None

    return {
        "model": model,
        "size_gb": info.get("size_gb"), "gpu_pct": info.get("gpu_pct"), "caps": caps, "load_s": load_s,
        "line_median_s": round(statistics.median(r["wall_s"] for r in dl), 2) if dl else None,
        "line_prompt_tokens": _med([r.get("prompt_tokens") for r in dl if r.get("rep") == 0]),
        "exchange_prompt_tokens": _med([r.get("prompt_tokens") for r in ex]),
        "exchange_out_tokens": _med([r.get("gen_tokens") for r in ex]),
        "gen_tps": _med([r.get("gen_tps") for r in dl]),
        "prefill_tps": _med([r.get("prefill_tps") for r in dl]),
        "exchange_fast_s": round(statistics.median(r["wall_s"] for r in ex if r.get("wall_s") is not None), 2)
        if any(r.get("wall_s") is not None for r in ex) else None,
        "exchange_legacy_s": lg[0]["wall_s"] if lg else None,
        "rule_ok_pct": rate(dl, "ok"),
        "polite_pct": rate(dl, "polite"),
        "foreign_pct": round(100 * sum(1 for r in dl if r.get("han") or r.get("kana")) / len(dl)) if dl else None,
        "formal_pct": rate(dl, "formal"),
        "filler_pct": rate(dl, "filler"),
        "raw_long_pct": round(100 * sum(1 for r in dl if (r.get("raw_sentences") or 0) > 2) / len(dl)) if dl else None,
        "avg_len": round(statistics.mean(r["length"] for r in dl if "length" in r)) if dl else None,
        "json_ok_pct": rate(ex + rc, "json_ok"),
        "exchange_ok_pct": rate(ex, "ok"),
        "judge_ok_pct": round(100 * judge_rate),
        "cpu_only": cpu,
    }


def recommend(summaries):
    good = [s for s in summaries if not s.get("error") and (s.get("rule_ok_pct") or 0) >= 85
            and (s.get("json_ok_pct") or 0) >= 90 and s.get("line_median_s")]
    if good:
        best = min(good, key=lambda s: (s["exchange_fast_s"] or 1e9, s["line_median_s"]))
        why = "규칙 준수 85%↑·JSON 90%↑ 중 NPC 대화가 가장 빠른 모델"
    else:
        ok = [s for s in summaries if not s.get("error") and s.get("rule_ok_pct") is not None]
        if not ok:
            return None, "측정된 모델이 없음"
        best = max(ok, key=lambda s: (s["rule_ok_pct"], -(s["line_median_s"] or 1e9)))
        why = "기준을 넘는 모델이 없어 규칙 준수율이 가장 높은 모델"
    return best, why


# ── 리포트 ───────────────────────────────────────────────────


def write_report(outdir, sysinfo, summaries, rows, labels):
    best, why = recommend(summaries)
    e = html.escape

    def cell(v, suffix="", bad=None, good=None):
        if v is None:
            return "<td class=muted>–</td>"
        cls = ""
        if bad is not None and v >= bad:
            cls = " class=bad"
        elif good is not None and v >= good:
            cls = " class=good"
        return f"<td{cls}>{e(str(v))}{suffix}</td>"

    head = ("<tr><th>모델</th><th>메모리</th><th>GPU</th><th>로드</th><th>대사 1줄<br><small>중앙값</small></th>"
            "<th>NPC 대화 묶음<br><small>빠른 모드</small></th><th>예전 방식</th><th>생성<br><small>tok/s</small></th>"
            "<th>프롬프트<br><small>tok/s</small></th><th>처리한 프롬프트<br><small>대사 / 묶음 토큰</small></th>"
            "<th>묶음 출력<br><small>토큰</small></th><th>규칙 준수</th><th>존댓말</th><th>외국어 혼입</th>"
            "<th>번역투</th><th>JSON</th><th>판정 형식</th><th>평균 길이</th></tr>")
    body = []
    for s in summaries:
        if s.get("error"):
            body.append(f"<tr><td><b>{e(s['model'])}</b><br><small>{e(labels.get(s.get('model_name') or s['model'], ''))}</small></td>"
                        f"<td colspan=17 class=bad>실패: {e(s['error'])}</td></tr>")
            continue
        mark = " ★" if best and s["model"] == best["model"] else ""
        body.append(
            f"<tr><td><b>{e(s['model'])}{mark}</b><br><small>{e(labels.get(s.get('model_name') or s['model'], ''))}</small></td>"
            + cell(s["size_gb"], "GB") + cell(s["gpu_pct"], "%") + cell(s["load_s"], "s")
            + cell(s["line_median_s"], "s") + cell(s["exchange_fast_s"], "s") + cell(s["exchange_legacy_s"], "s")
            + cell(s["gen_tps"]) + cell(s["prefill_tps"])
            + f"<td>{e(str(s.get('line_prompt_tokens') or '–'))} / {e(str(s.get('exchange_prompt_tokens') or '–'))}</td>"
            + cell(s.get("exchange_out_tokens"))
            + cell(s["rule_ok_pct"], "%", good=85) + cell(s["polite_pct"], "%", good=90)
            + cell(s["foreign_pct"], "%", bad=1) + cell(s["formal_pct"], "%", bad=10)
            + cell(s["json_ok_pct"], "%", good=90) + cell(s["judge_ok_pct"], "%", good=90) + cell(s["avg_len"], "자")
            + "</tr>")
        if s.get("cpu_only"):
            c = s["cpu_only"]
            body.append(f"<tr class=sub><td colspan=18>└ CPU 전용이면: 대사 {c['wall_s']}s · 생성 {c['gen_tps']} tok/s · "
                        f"프롬프트 {c['prefill_tps']} tok/s</td></tr>")

    # 케이스별 출력 비교
    models = [s["model"] for s in summaries if not s.get("error")]
    cases = []
    for r in rows:
        key = (r["case"], r["desc"])
        if key not in cases:
            cases.append(key)
    samples = ["<tr><th>케이스</th>" + "".join(f"<th>{e(m)}</th>" for m in models) + "</tr>"]
    for cid, desc in cases:
        tds = []
        for m in models:
            rs = [r for r in rows if r["model"] == m and r["case"] == cid]
            parts = []
            for r in rs:
                okm = "" if r.get("ok") is None else ("<span class=good>✓</span> " if r.get("ok") else "<span class=bad>✗</span> ")
                parts.append(f"<div>{okm}{e(r.get('text') or '')} <small class=muted>{r.get('wall_s')}s</small></div>")
            tds.append("<td class=txt>" + "".join(parts) + "</td>")
        samples.append(f"<tr><th class=case>{e(desc)}<br><small>{e(cid)}</small></th>{''.join(tds)}</tr>")

    rec = ""
    if best:
        mn = best.get("model_name") or best["model"]
        rec = (f"<div class=rec><b>추천: {e(best['model'])}</b> — {e(why)}<br>"
               f"main.py 설정: <code>MODEL_NAME = \"{e(mn)}\"</code>, "
               f"<code>JUDGE_MODEL = \"{e(mn)}\"</code>, <code>SEVENS_PROMPT = {e(best.get('style') or 'compact')}</code> "
               f"(판정 형식 {best['judge_ok_pct']}% — 90% 미만이면 판정은 기존 모델 유지 권장)<br>"
               f"<small>숫자는 참고용이에요. 아래 실제 대사를 읽고 캐릭터 말투가 살아 있는지 꼭 직접 확인하세요.</small></div>")

    doc = f"""<!doctype html><html lang=ko><meta charset=utf-8><meta name=viewport content="width=device-width,initial-scale=1">
<title>모델 벤치마크</title>
<style>
:root{{--bg:#fff;--fg:#1d1d1f;--muted:#6e6e73;--line:#e5e5ea;--good:#1a7f37;--bad:#c62828;--hl:#f5f7ff}}
@media (prefers-color-scheme:dark){{:root{{--bg:#16161a;--fg:#ececf1;--muted:#9a9aa3;--line:#2c2c33;--good:#5cc98a;--bad:#ff7b7b;--hl:#1e2130}}}}
body{{font:14px/1.5 -apple-system,"Malgun Gothic",sans-serif;background:var(--bg);color:var(--fg);margin:24px;}}
h1{{font-size:20px}} h2{{font-size:16px;margin-top:32px}} small,.muted{{color:var(--muted)}}
table{{border-collapse:collapse;width:100%;margin:8px 0}} th,td{{border:1px solid var(--line);padding:6px 8px;vertical-align:top;text-align:left}}
th{{background:var(--hl);font-weight:600}} .good{{color:var(--good);font-weight:600}} .bad{{color:var(--bad);font-weight:600}}
td.txt div{{margin-bottom:4px}} th.case{{width:140px}} tr.sub td{{color:var(--muted);font-size:12px;border-top:none}}
.rec{{background:var(--hl);border:1px solid var(--line);padding:12px;border-radius:8px;margin:12px 0}} code{{font-size:12px}}
.wrap{{overflow-x:auto}}
</style>
<h1>SEVENS 캐릭터 대사 모델 벤치마크</h1>
<p class=muted>{e(sysinfo['time'])} · {e(sysinfo['cpu'])} · RAM {e(str(sysinfo['ram_gb']))}GB · Ollama {e(sysinfo['ollama'])} · 반복 {sysinfo['reps']}회 · num_ctx {srv.NUM_CTX}</p>
{rec}
<h2>요약</h2>
<div class=wrap><table>{head}{''.join(body)}</table></div>
<p class=muted>규칙 준수 = 비어 있지 않음 · 중국어/일본어 없음 · 모든 문장이 ~요/~죠로 끝남 · ~니다 없음 · 80자 이하 (서버 후처리 후 기준).
NPC 대화 묶음 = /conversation/exchange 한 번에 플레이어가 기다리는 시간. 예전 방식은 같은 모델로 상황+대사 3줄(4회 호출)을 잰 값.
GPU = 모델이 GPU(Vulkan 등) 메모리에 올라간 비율.</p>
<h2>실제 대사 비교</h2>
<div class=wrap><table>{''.join(samples)}</table></div>
</html>"""
    path = os.path.join(outdir, "report.html")
    with open(path, "w", encoding="utf-8") as f:
        f.write(doc)
    return path, best


def system_info(reps):
    cpu = platform.processor()
    ram = None
    try:
        if os.name == "nt":
            import winreg
            import ctypes
            k = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"HARDWARE\DESCRIPTION\System\CentralProcessor\0")
            cpu = winreg.QueryValueEx(k, "ProcessorNameString")[0].strip()

            class MS(ctypes.Structure):
                _fields_ = [("dwLength", ctypes.c_ulong), ("dwMemoryLoad", ctypes.c_ulong)] + \
                           [(n, ctypes.c_ulonglong) for n in ("tp", "ap", "tpf", "apf", "tv", "av", "aev")]
            s = MS()
            s.dwLength = ctypes.sizeof(s)
            ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(s))
            ram = round(s.tp / 2**30, 1)
        elif os.path.exists("/proc/meminfo"):
            kb = int(open("/proc/meminfo").read().split("MemTotal:")[1].split()[0])
            ram = round(kb / 2**20, 1)
    except Exception:
        pass
    try:
        ver = api("GET", "/api/version")["version"]
    except Exception:
        ver = "?"
    return {"time": datetime.now().strftime("%Y-%m-%d %H:%M"), "cpu": cpu, "ram_gb": ram, "ollama": ver, "reps": reps}


def main():
    ap = argparse.ArgumentParser(description="SEVENS 캐릭터 대사 모델 벤치마크")
    ap.add_argument("--models", help="쉼표로 구분한 모델 목록(기본: 추천 후보)")
    ap.add_argument("--with-optional", action="store_true", help="큰 후보(gemma4:e4b, qwen3.5:9b)도 포함")
    ap.add_argument("--reps", type=int, default=2, help="케이스당 반복 횟수(기본 2)")
    ap.add_argument("--quick", action="store_true", help="빠르게: 반복 1회, 케이스 일부")
    ap.add_argument("--pull", action="store_true", help="없는 모델을 받아서 측정")
    ap.add_argument("--cpu-compare", action="store_true", help="GPU를 쓰는 모델은 CPU 전용 속도도 측정")
    ap.add_argument("--prompt", choices=["compact", "full", "both"], default="compact",
                    help="프롬프트 방식: compact(기본, 서버 기본값) / full(예전) / both(둘 다 비교)")
    args = ap.parse_args()
    reps = 1 if args.quick else args.reps

    try:
        api("GET", "/api/version", timeout=5)
    except Exception:
        print(f"Ollama({OLLAMA})에 연결할 수 없어요. 'ollama serve'가 켜져 있는지 확인하세요.")
        sys.exit(1)
    try:
        requests.get("http://localhost:8000/", timeout=1)
        print("※ SEVENS 서버(uvicorn)가 켜져 있어요. 측정이 정확하려면 끄고 다시 실행하는 걸 권장해요.\n")
    except Exception:
        pass

    cands = CANDIDATES + (OPTIONAL if args.with_optional else [])
    labels = dict(cands)
    models = [m.strip() for m in args.models.split(",")] if args.models else [m for m, _ in cands]
    installed = {m["name"] for m in api("GET", "/api/tags").get("models", [])}
    installed |= {n.split(":")[0] + ":latest" for n in installed if ":" not in n}

    def is_installed(m):
        return m in installed or (":" not in m and f"{m}:latest" in installed)

    missing = [m for m in models if not is_installed(m)]
    if missing and not args.pull:
        print("아직 받지 않은 모델은 건너뛰어요. 받으려면 아래를 실행하거나 --pull 을 붙이세요:")
        for m in missing:
            print(f"  ollama pull {m}")
        models = [m for m in models if m not in missing]
    elif missing:
        for m in missing:
            try:
                pull(m)
            except Exception as e:
                print(f"   받기 실패({m}): {e}")
    if not models:
        print("측정할 모델이 없어요.")
        sys.exit(1)

    sysinfo = system_info(reps)
    outdir = os.path.join(HERE, "bench_results", datetime.now().strftime("%Y%m%d-%H%M"))
    os.makedirs(outdir, exist_ok=True)
    print(f"측정 모델 {len(models)}개 · 반복 {reps}회 · 프롬프트 {args.prompt} · 결과 폴더 {outdir}")
    print(f"{sysinfo['cpu']} · RAM {sysinfo['ram_gb']}GB · Ollama {sysinfo['ollama']}")

    summaries, all_rows = [], []
    for m in models:
        styles = ("compact", "full") if args.prompt == "both" else (args.prompt,)
        s, rows = bench_model(m, reps, args.quick, args.cpu_compare, styles)
        summaries.extend(s)
        all_rows += rows
        # 모델마다 중간 저장(도중에 멈춰도 결과가 남도록)
        with open(os.path.join(outdir, "summary.json"), "w", encoding="utf-8") as f:
            json.dump({"system": sysinfo, "summaries": summaries}, f, ensure_ascii=False, indent=2)
        keys = sorted({k for r in all_rows for k in r})
        with open(os.path.join(outdir, "results.csv"), "w", encoding="utf-8-sig", newline="") as f:
            w = csv.DictWriter(f, fieldnames=keys)
            w.writeheader()
            w.writerows(all_rows)
        write_report(outdir, sysinfo, summaries, all_rows, labels)

    path, best = write_report(outdir, sysinfo, summaries, all_rows, labels)
    print(f"\n완료! 리포트: {path}")
    if best:
        print(f"추천: {best['model']}  (대사 {best['line_median_s']}s, NPC 대화 {best['exchange_fast_s']}s, 규칙 {best['rule_ok_pct']}%)")
    try:
        if os.name == "nt":
            os.startfile(path)
    except Exception:
        pass


if __name__ == "__main__":
    main()
