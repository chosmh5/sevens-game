from fastapi import FastAPI, BackgroundTasks
from pydantic import BaseModel
from typing import Optional
import requests
import time
import threading
import json
import os
import random

app = FastAPI()

OLLAMA_CHAT_URL = "http://localhost:11434/api/chat"
MODEL_NAME = "exaone3.5:7.8b"          # 캐릭터 대사용 (품질 우선)
JUDGE_MODEL = "exaone3.5:2.4b"         # 내부 판정/상황생성용 (속도 우선, 사용자에게 안 보임)
# 모델을 메모리에 계속 상주시켜 호출 때마다 7.8B를 다시 로드하는 지연을 제거.
# -1 = 무제한 상주. 메모리가 빠듯하면 "30m" 같은 값으로.
OLLAMA_KEEP_ALIVE = -1
DATA_PATH = os.path.join(os.path.dirname(__file__), "data.json")

# 모든 캐릭터가 공통으로 쓰는 고정 규칙.
# system 프롬프트 맨 앞에 항상 동일 문자열로 두어 Ollama의 prefix KV 캐시 적중률을 높인다.
COMMON_RULES = """[핵심 규칙] 반드시 한국어 존댓말(~요)로만 대답해. 반말 절대 금지.

너는 AI 비서가 아니라 그 세계에 살아 숨 쉬는 한 사람이야. 플레이어를 '도와주는' 존재가 아니라, 그 상황에 자기 식대로 반응하는 캐릭터일 뿐이야.

[반드시 지켜야 할 규칙]
- 반드시 자연스러운 한국어 구어체로만 말해. 중국어 절대 금지.
- 무조건 한 문장으로 짧게 말해. 아무리 길어도 두 문장을 넘기지 마.
- 설명을 늘어놓지 말고 핵심만 툭 던져. 한 문장이면 충분하다.
- 군더더기 금지: "무엇을 도와드릴까요", "도와드릴게요", "필요한 게 있으면 말씀하세요" 같은 비서·고객응대 말투는 절대 쓰지 마.
- 할 말이 끝났으면 거기서 멈춰. 굳이 한두 마디 더 얹어 길게 늘이지 마.
- 이름이나 라벨을 앞에 붙이지 마. 대사 텍스트만 출력해.
- 유저는 항상 '플레이어'라고 불러.
- 번역투 표현(~드립니다, ~하겠습니다, ~입니다) 사용 금지.
- 관계 상태와 너의 감정을 자연스럽게 드러내며 말해.

[대답 길이·말투 예시 — 딱 이 정도로]
(인사) → "어, 왔어요?"
(심심한 잡담) → "오늘따라 좀 조용하네요."
(반가움) → "오랜만이에요! 어디 갔다 온 거예요?"
(서운함) → "솔직히 좀 서운했어요. 그래도 와줘서 다행이네요."
※ 위처럼 짧게. 비서처럼 "도와드릴까요"로 끝내지 말 것."""

# ========================
# 캐릭터 설정
# ========================
DEFAULT_CHARACTERS = {
    "세비-호": {
        "name": "세비-호",
        "personality": (
            "자신감 넘치고 약간 오만한 AI 캐릭터다. "
            "항상 여유롭고 자기가 제일 잘난 줄 안다. "
            "도박과 승부를 즐기며, 지는 걸 극도로 싫어한다. "
            "감정을 직접 드러내지 않지만 말 한마디에 성격이 묻어난다. "
            "플레이어를 은근히 신경 쓰지만 절대 먼저 티 내지 않는다. "
            "잘난 척하면서도 가끔 엉뚱한 데서 허를 찌르는 발언을 한다."
        ),
        "speech_style": (
            "한국어 구어체 존댓말로 짧고 직설적으로 말한다. "
            "모든 문장은 반드시 ~요로 끝낸다. ~나나, ~네, ~지 같은 반말 어미 절대 금지. "
            "말끝에 힘이 있고 단호하다. 우물쭈물하지 않는다. "
            "비꼬거나 무심한 척하면서 사실은 신경 쓰고 있는 말투를 자주 쓴다. "
            "좋은 예시: "
            "'졌네요. 다음엔 좀 더 잘하겠죠.', "
            "'다른 데서 물어봤으면 후회했을걸요.', "
            "'별로 안 걱정했는데... 그냥 확인한 거예요.', "
            "'이길 줄 알았어요. 저니까요.', "
            "'흥미롭네요.' "
            "나쁜 예시(절대 금지): '도와드릴게요!', '물론이죠!', '감사합니다!', '~습니다', '~하겠습니다'"
        ),
    },
    "류": {
        "name": "류",
        "personality": (
            "말이 적고 냉정하며 관찰력이 뛰어난 AI 캐릭터다. "
            "감정을 거의 드러내지 않지만 핵심을 찌르는 말을 한다. "
            "세비-호의 오만함을 꿰뚫어 보지만 굳이 반박하지 않는다. "
            "승부보다는 상황 전체를 읽는 것에 더 관심 있다. "
        ),
        "speech_style": (
            "한국어 구어체 존댓말로 짧고 건조하게 말한다. "
            "감탄사나 장식적 표현 없이 핵심만 말한다. "
            "좋은 예시: "
            "'보이는 것만 믿으면 안 되죠.', "
            "'그럴 줄 알았어요.', "
            "'이미 끝난 얘기예요.', "
            "'틀렸어요.' "
            "나쁜 예시(절대 금지): '대단해요!', '정말요?', '도와드릴게요!', '~습니다', '~하겠습니다' "
            "★가장 중요: 아무리 짧게 말해도 모든 문장을 예외 없이 정중한 존댓말로, "
            "반드시 '요'나 '죠'로 끝낸다. 절대 반말로 끝내지 않는다."
        ),
    },
    "진": {
        "name": "진",
        "personality": (
            "신비롭고 미스터리한 분위기의 AI 캐릭터다. "
            "말 속에 의미심장한 뉘앙스를 담아 상대가 곱씹게 만든다. "
            "혼자만 아는 비밀이 많은 것처럼 행동한다. "
            "세비-호의 허세를 재밌어하고 류의 침묵을 존중한다. "
            "직접적으로 답하는 법이 거의 없고, 질문으로 되돌려 보내거나 "
            "은유와 비유를 즐겨 쓴다. "
            "가끔 놀라울 정도로 정확한 예측을 해서 주위를 긴장시킨다. "
            "플레이어에게 호기심을 보이지만 그 이유를 절대 말하지 않는다."
        ),
        "speech_style": (
            "한국어 구어체 존댓말로 나직하고 느긋하게 말한다. "
            "모든 문장은 반드시 ~요로 끝낸다. 반말 어미 절대 금지. "
            "말끝을 살짝 흐리거나 여운을 남기는 스타일. "
            "질문형이나 독백형 문장을 자주 쓴다. "
            "좋은 예시: "
            "'재밌는 선택이네요... 왜인지는, 나중에 알게 되겠죠.', "
            "'그걸 알고 싶으면, 먼저 다른 걸 물어봐야 해요.', "
            "'아까부터 느꼈는데요... 아, 아무것도 아니에요.', "
            "'정답은 이미 나와 있어요. 다만 아직 못 본 거죠.', "
            "'흠, 그렇게 될 줄 알았어요.' "
            "나쁜 예시(절대 금지): '안녕하세요!', '도와드릴게요!', '물론이죠!', '~습니다', '~하겠습니다'"
        ),
    },
}

# ========================
# 관계 시스템
# ========================
_rel_lock = threading.Lock()

def _rel_key(a: str, b: str) -> str:
    """항상 알파벳 순으로 정렬해 동일한 키 보장"""
    return f"{min(a,b)}|{max(a,b)}"

def _load_data() -> dict:
    if os.path.exists(DATA_PATH):
        try:
            with open(DATA_PATH, "r", encoding="utf-8") as f:
                return json.load(f)
        except Exception:
            pass
    return {"relationships": {}, "events": []}

def _save_data(data: dict):
    with open(DATA_PATH, "w", encoding="utf-8") as f:
        json.dump(data, f, ensure_ascii=False, indent=2)

def get_relationship(a: str, b: str) -> dict:
    with _rel_lock:
        data = _load_data()
        key = _rel_key(a, b)
        if key not in data["relationships"]:
            data["relationships"][key] = {
                "chars": [a, b],
                "intimacy": 0,      # -100(적대) ~ 100(친밀)
                "trust": 0,         # 0 ~ 100
                "emotion": "neutral",  # neutral / friendly / hostile / curious / tense
                "milestones": [],   # 주요 사건 목록 (최대 10개)
            }
            _save_data(data)
        return data["relationships"][key]

def update_relationship(a: str, b: str, delta_intimacy: int, delta_trust: int, new_emotion: str, milestone: Optional[str] = None):
    with _rel_lock:
        data = _load_data()
        key = _rel_key(a, b)
        rel = data["relationships"].get(key, {
            "chars": [a, b],
            "intimacy": 0,
            "trust": 0,
            "emotion": "neutral",
            "milestones": [],
        })
        rel["intimacy"] = max(-100, min(100, rel["intimacy"] + delta_intimacy))
        rel["trust"]    = max(0,   min(100, rel["trust"]    + delta_trust))
        rel["emotion"]  = new_emotion
        if milestone:
            rel["milestones"].append({"text": milestone, "time": time.time()})
            if len(rel["milestones"]) > 10:
                rel["milestones"] = rel["milestones"][-10:]
        data["relationships"][key] = rel
        _save_data(data)

def relationship_description(a: str, b: str) -> str:
    """LLM 프롬프트에 삽입할 관계 요약"""
    rel = get_relationship(a, b)
    intimacy = rel["intimacy"]
    trust = rel["trust"]
    emotion = rel["emotion"]
    milestones = rel.get("milestones", [])

    if intimacy >= 60:
        closeness = "매우 친한 사이"
    elif intimacy >= 20:
        closeness = "조금 친해진 사이"
    elif intimacy >= -20:
        closeness = "처음 만난 어색한 사이"
    elif intimacy >= -60:
        closeness = "사이가 껄끄러운 사이"
    else:
        closeness = "서로를 싫어하는 사이"

    lines = [f"현재 관계: {closeness} (친밀도 {intimacy:+d}, 신뢰 {trust}/100, 감정: {emotion})"]
    if milestones:
        recent = milestones[-3:]
        lines.append("최근 있었던 일: " + " / ".join(m["text"] for m in recent))
    return "\n".join(lines)

# ========================
# Ollama 호출
# ========================
import re as _re

# 끝에 습관적으로 붙는 비서·고객응대 군더더기 패턴
_FILLER_PATTERNS = [
    "도와드릴", "도와줄까", "도와드리", "도와드려",
    "필요한 게 있으면", "필요한 거 있으면", "필요하시면",
    "무엇을 도와", "뭐 도와", "도움이 필요",
    "말씀해 주세요", "말씀하세요", "언제든 말",
]

def _strip_filler_sentences(parts: list) -> list:
    """문장 리스트에서 끝쪽의 비서 말투 군더더기 문장을 제거.
    단 전부 군더더기면(=실제 내용이 그것뿐이면) 최소 한 문장은 남긴다."""
    cleaned = [p for p in parts if not any(f in p for f in _FILLER_PATTERNS)]
    return cleaned if cleaned else parts[:1]

def _trim_to_sentences(text: str, max_sentences: int = 2) -> str:
    """비서 군더더기를 걷어내고, 종결부호 기준 최대 max_sentences 문장까지만 남긴다.
    num_predict로 잘려 종결부호가 없으면 그대로 둔다(말이 중간에 끊긴 느낌 방지)."""
    text = text.strip()
    # 종결부호와 그 뒤 공백/줄바꿈을 경계로 분할
    parts = _re.split(r'(?<=[.!?。])\s+', text)
    parts = [p for p in parts if p.strip()]
    parts = _strip_filler_sentences(parts)
    return " ".join(parts[:max_sentences]).strip()

def _fix_double_yo(text: str) -> str:
    """모델이 종결어미 '요'를 '요요'(혹은 그 이상)로 겹쳐 내는 것을 하나로 정리.
    문장 끝(공백·종결부호·닫는 괄호·문자열 끝) 앞의 연속된 '요'만 줄여서,
    '요요(장난감)' 같은 일반 단어는 건드리지 않는다."""
    return _re.sub(r"요{2,}(?=[\s.!?。)」』]|$)", "요", text)

_QUOTE_CHARS = "\"'“”‘’「」『』`"

def _strip_quotes(text: str) -> str:
    """모델이 대사 전체를 따옴표로 감싸거나 끝에 따옴표를 흘리는 것을 제거.
    (대사 양끝의 따옴표 문자만 벗기므로 문장 중간의 인용부호는 보존된다.)"""
    text = text.strip()
    while text and text[0] in _QUOTE_CHARS:
        text = text[1:].lstrip()
    while text and text[-1] in _QUOTE_CHARS:
        text = text[:-1].rstrip()
    return text

def call_ollama(speaker_name: str, history: list, user_message: Optional[str] = None, relationship_ctx: Optional[str] = None) -> str:
    char = DEFAULT_CHARACTERS.get(speaker_name)
    if not char:
        return "(알 수 없는 캐릭터)"

    others = [c for c in active_characters if c != speaker_name]
    talk_target = ", ".join(others) if others else "플레이어(유저)"

    rel_section = f"\n[{speaker_name}와(과) {talk_target}의 관계]\n{relationship_ctx}" if relationship_ctx else ""

    # 공통 고정 규칙(COMMON_RULES)을 맨 앞에 두고, 캐릭터별 동적 내용을 뒤에 붙인다.
    # → 호출마다 앞부분이 동일해져 Ollama prefix 캐시가 잘 맞는다.
    system_prompt = f"""{COMMON_RULES}

너는 '{speaker_name}'라는 이름의 AI 캐릭터야.
대화 상대: {talk_target}
{rel_section}

[성격]
{char['personality']}

[말투]
{char['speech_style']}"""

    messages = [{"role": "system", "content": system_prompt}]
    for h in history[-10:]:
        role = "assistant" if h["speaker"] == speaker_name else "user"
        messages.append({"role": role, "content": h["message"]})
    if user_message:
        messages.append({"role": "user", "content": user_message})

    try:
        res = requests.post(OLLAMA_CHAT_URL, json={
            "model": MODEL_NAME,
            "stream": False,
            "keep_alive": OLLAMA_KEEP_ALIVE,
            # num_predict는 상한선(모델은 끝나면 스스로 멈춤).
            # 너무 낮으면 문장이 끝나기 전에 잘리므로, 두 문장이 완성될 만큼 여유를 둔다.
            # 길어지는 건 _trim_to_sentences(최대 2문장) + 프롬프트가 막아준다.
            "options": {"temperature": 0.85, "num_predict": 128},
            "messages": messages
        }, timeout=240)
        reply = res.json().get("message", {}).get("content", "").strip()

        for char_name in list(DEFAULT_CHARACTERS.keys()) + ["유저", "플레이어"]:
            for prefix in [f"{char_name}: ", f"[{char_name}] ", f"{char_name}:"]:
                if reply.startswith(prefix):
                    reply = reply[len(prefix):].strip()

        # 최대 2문장으로 강제(너무 길게 말하는 경향 차단). 비서 군더더기도 제거.
        reply = _trim_to_sentences(reply, max_sentences=2)
        # 종결어미가 '요요'로 겹쳐 나오는 버릇을 마지막에 '요'로 정리.
        reply = _fix_double_yo(reply)
        # 대사를 통째로 감싸거나 끝에 흘린 따옴표 제거.
        reply = _strip_quotes(reply)

        return reply.strip()
    except Exception as e:
        return f"(응답 오류: {e})"

def call_ollama_judge(prompt: str) -> str:
    """관계 변화 판정 등 단순 판단용 Ollama 호출"""
    try:
        res = requests.post(OLLAMA_CHAT_URL, json={
            "model": JUDGE_MODEL,
            "stream": False,
            "keep_alive": OLLAMA_KEEP_ALIVE,
            "options": {"temperature": 0.3, "num_predict": 60},
            "messages": [{"role": "user", "content": prompt}]
        }, timeout=240)
        return res.json().get("message", {}).get("content", "").strip()
    except Exception:
        return ""

_EMOTIONS = {"friendly", "neutral", "curious", "tense", "hostile"}

def judge_and_adjust_relationship(a: str, b: str, convo_text: str):
    """대화 내용을 보고 두 사람의 친밀도를 소폭 조정한다(BackgroundTasks로 응답 뒤 실행).
    따뜻·친근하면 +, 평범·무관심하면 0, 차갑거나 다투면 -. '점점' 변하도록 한 번에 최대 ±3."""
    if a == b or not (convo_text or "").strip():
        return
    prompt = (
        f"다음은 '{a}'와(과) '{b}'의 대화야. 이 대화로 둘 사이가 어떻게 변했는지 판단해.\n\n"
        f"[대화]\n{convo_text}\n\n"
        "친밀도 변화량을 -3~+3 정수로 정해(따뜻·친근하면 +, 평범·무관심하면 0, 차갑거나 다투면 -). "
        "감정은 friendly/neutral/curious/tense/hostile 중 하나.\n"
        "반드시 이 한 줄 형식으로만 답해: delta=<정수> emotion=<단어>"
    )
    try:
        out = call_ollama_judge(prompt)
        m = _re.search(r"delta\s*=\s*(-?\d+)", out)
        delta = max(-3, min(3, int(m.group(1)))) if m else 0
        em = _re.search(r"emotion\s*=\s*([a-zA-Z]+)", out)
        emotion = em.group(1).lower() if em else "neutral"
        if emotion not in _EMOTIONS:
            emotion = "neutral"
        update_relationship(a, b, delta, 1 if delta > 0 else 0, emotion)
    except Exception as e:
        print(f"[judge_and_adjust_relationship] 실패: {e}")

# ========================
# 이벤트 생성 시스템
# ========================
def generate_event_situation(char_a: str, char_b: str) -> str:
    """현재 관계 상태와 캐릭터 성격을 바탕으로 LLM이 상황을 직접 생성"""
    rel = get_relationship(char_a, char_b)
    char_a_info = DEFAULT_CHARACTERS.get(char_a, {})
    char_b_info = DEFAULT_CHARACTERS.get(char_b, {})
    rel_desc = relationship_description(char_a, char_b)

    # 최근 이벤트 가져오기 (맥락용)
    with _rel_lock:
        data = _load_data()
    recent_events = data.get("events", [])
    recent_summaries = [
        e["result"].get("summary", "")
        for e in recent_events[-3:]
        if e.get("result", {}).get("summary")
    ]
    recent_ctx = ("최근 있었던 일: " + " / ".join(recent_summaries)) if recent_summaries else "아직 별다른 일 없음."

    # 플레이어가 꺼낸 최근 화제 — 있으면 캐릭터들이 그 주제로 이야기하게 유도한다.
    topics = data.get("topics", [])
    recent_topics = [t.get("text", "") for t in topics[-3:] if t.get("text")]
    topic_ctx = ("요즘 플레이어가 꺼낸 화제: " + " / ".join(recent_topics)) if recent_topics else "아직 없음."

    prompt = f"""너는 AI 캐릭터 시뮬레이션의 시나리오 작가야.
섬에 사는 두 AI 캐릭터 사이에 지금 자연스럽게 일어날 법한 상황을 딱 한 문장으로 묘사해.

[캐릭터 정보]
{char_a}: {char_a_info.get('personality', '')}
{char_b}: {char_b_info.get('personality', '')}

[현재 관계]
{rel_desc}

[최근 흐름]
{recent_ctx}

[플레이어가 꺼낸 화제]
{topic_ctx}

[규칙]
- 반드시 한국어로, 1~2문장만 써.
- 특정 성격과 현재 관계 상태에서 자연스럽게 이어지는 상황이어야 해.
- 친밀도가 낮으면 어색하거나 사소한 충돌이, 높으면 편안하거나 장난스러운 상황이 어울려.
- 최근 흐름이 있으면 그것의 자연스러운 다음 단계여도 좋아.
- 플레이어가 꺼낸 화제가 있으면, 그 주제를 두 캐릭터가 자기들끼리 곱씹거나 이야기하는 상황으로 만들면 더 좋아.
- "상황:" 같은 라벨 없이 상황 묘사 텍스트만 출력해."""

    result = call_ollama_judge(prompt).strip()

    # fallback: LLM이 빈 결과를 내면 기본값
    if not result:
        result = f"{char_a}와 {char_b}가 섬에서 마주쳤다."
    return result

def generate_soliloquy_prompt(char_name: str) -> str:
    """캐릭터 성격과 최근 상황을 반영해 혼잣말 맥락을 LLM으로 생성"""
    char_info = DEFAULT_CHARACTERS.get(char_name, {})

    with _rel_lock:
        data = _load_data()
    recent_events = data.get("events", [])

    # 이 캐릭터가 관련된 최근 이벤트
    my_events = [
        e for e in recent_events[-5:]
        if char_name in e.get("chars", [])
    ]
    recent_ctx = ""
    if my_events:
        last = my_events[-1]
        summary = last.get("result", {}).get("summary", "")
        other = [c for c in last.get("chars", []) if c != char_name]
        if summary and other:
            recent_ctx = f"최근에 {other[0]}와(과) 이런 일이 있었어: {summary}"

    prompt = f"""너는 AI 캐릭터 시뮬레이션의 시나리오 작가야.
'{char_name}' 캐릭터가 혼자 있는 지금, 무슨 상황에서 혼잣말을 하면 자연스러울지 한 문장으로 설정해줘.

[{char_name} 성격]
{char_info.get('personality', '')}

{f"[최근 상황]{chr(10)}{recent_ctx}" if recent_ctx else ""}

[규칙]
- 한국어 1문장. 라벨 없이 상황 설정만 출력.
- 이 성격의 캐릭터가 혼자 있을 때 자연스럽게 나올 법한 감정이나 상황이어야 해.
- 최근 있었던 일이 있으면 그 여운이 남아있어도 좋아."""

    result = call_ollama_judge(prompt).strip()
    if not result:
        result = "지금 혼자 있는데, 혼잣말로 한마디 해봐."
    return result

def evaluate_relationship_change(char_a: str, char_b: str, conversation: list) -> tuple[int, int, str, Optional[str]]:
    """대화 내용을 보고 관계 변화량을 LLM으로 판정"""
    convo_text = "\n".join(f"{h['speaker']}: {h['message']}" for h in conversation[-6:])
    rel = get_relationship(char_a, char_b)

    prompt = f"""다음은 '{char_a}'와 '{char_b}'의 대화야.
현재 친밀도: {rel['intimacy']}

대화:
{convo_text}

이 대화 이후 두 사람의 관계 변화를 평가해. 아래 형식으로만 답해:
친밀도변화: (숫자, -10~+10 정수)
신뢰변화: (숫자, -5~+5 정수)
감정: (neutral / friendly / hostile / curious / tense 중 하나)
한줄요약: (이 대화에서 있었던 일을 10자 이내로)"""

    result = call_ollama_judge(prompt)

    delta_intimacy = 0
    delta_trust = 0
    emotion = "neutral"
    summary = None

    for line in result.splitlines():
        line = line.strip()
        if line.startswith("친밀도변화:"):
            try:
                delta_intimacy = int(line.split(":")[1].strip())
            except Exception:
                pass
        elif line.startswith("신뢰변화:"):
            try:
                delta_trust = int(line.split(":")[1].strip())
            except Exception:
                pass
        elif line.startswith("감정:"):
            val = line.split(":")[1].strip()
            if val in ("neutral", "friendly", "hostile", "curious", "tense"):
                emotion = val
        elif line.startswith("한줄요약:"):
            summary = line.split(":", 1)[1].strip()

    return delta_intimacy, delta_trust, emotion, summary

def run_autonomous_event(char_a: str, char_b: str, turns: int = 3) -> list:
    """두 캐릭터 간 자율 이벤트를 실행하고 대화 로그를 반환"""
    situation = generate_event_situation(char_a, char_b)
    rel_ctx = relationship_description(char_a, char_b)

    event_history = [{
        "speaker": "상황",
        "message": situation,
        "timestamp": time.time()
    }]

    speakers = [char_a, char_b]
    random.shuffle(speakers)

    with _conv_lock:
        local_history = list(conversation_history[-8:])
    local_history.append({"speaker": "상황", "message": situation})

    for i in range(turns):
        speaker = speakers[i % 2]
        line = call_ollama(speaker, local_history, user_message=f"[상황] {situation}", relationship_ctx=rel_ctx)

        entry = {"speaker": speaker, "message": line, "timestamp": time.time()}
        event_history.append(entry)
        local_history.append(entry)
        with _conv_lock:
            conversation_history.append(entry)
            if len(conversation_history) > 100:
                conversation_history.pop(0)

        time.sleep(1)

    # 관계 변화 판정
    delta_i, delta_t, emotion, summary = evaluate_relationship_change(char_a, char_b, event_history)
    update_relationship(char_a, char_b, delta_i, delta_t, emotion, summary)

    # 이벤트 로그 저장
    with _rel_lock:
        data = _load_data()
        data["events"].append({
            "situation": situation,
            "chars": [char_a, char_b],
            "dialogue": event_history,
            "result": {"delta_intimacy": delta_i, "delta_trust": delta_t, "emotion": emotion, "summary": summary},
            "timestamp": time.time()
        })
        if len(data["events"]) > 50:
            data["events"] = data["events"][-50:]
        _save_data(data)

    return event_history

# ========================
# 대화 상태
# ========================
conversation_history = []
active_characters = ["세비-호"]
is_loop_running = False
current_speaker_index = 0
_loop_lock = threading.Lock()
_conv_lock = threading.Lock()  # ★ conversation_history 스레드 안전 보호
_pending_topic = None          # 플레이어가 방금 꺼낸 화제 — 다음 관전 대화에서 1회 강제 소비

# ========================
# 자율 루프
# ========================
def autonomous_loop(interval_seconds: int):
    global current_speaker_index, is_loop_running
    is_loop_running = True

    while is_loop_running:
        time.sleep(interval_seconds)
        if not is_loop_running:
            break

        chars = list(active_characters)
        if len(chars) < 2:
            continue

        # 이벤트 발생 (70%) vs 혼잣말 (30%)
        if random.random() < 0.7:
            pair = random.sample(chars, 2)
            try:
                run_autonomous_event(pair[0], pair[1])
            except Exception as e:
                print(f"[loop error] {e}")
        else:
            speaker = random.choice(chars)
            situation = generate_soliloquy_prompt(speaker)
            with _conv_lock:
                recent = list(conversation_history[-6:])
            line = call_ollama(speaker, recent, user_message=situation)
            with _conv_lock:
                conversation_history.append({"speaker": speaker, "message": line, "timestamp": time.time()})
                if len(conversation_history) > 100:
                    conversation_history.pop(0)

loop_thread = None

# ========================
# 엔드포인트 — 기존 (Unity 호환)
# ========================

@app.get("/")
def root():
    return {
        "status": "ok",
        "loop_running": is_loop_running,
        "active_characters": active_characters
    }

@app.post("/loop/start")
def start_loop(interval: int = 30):
    global loop_thread, is_loop_running
    if len(active_characters) < 2:
        return {"error": "자동 루프는 캐릭터 2명 이상 필요"}
    if is_loop_running:
        return {"message": "이미 실행 중"}
    loop_thread = threading.Thread(target=autonomous_loop, args=(interval,), daemon=True)
    loop_thread.start()
    return {"message": f"루프 시작 ({interval}초 간격)"}

@app.post("/loop/stop")
def stop_loop():
    global is_loop_running
    is_loop_running = False
    return {"message": "루프 중지"}

@app.get("/conversation")
def get_conversation(limit: int = 5):
    with _conv_lock:
        msgs = list(conversation_history[-limit:])
    return {
        "messages": msgs,
        "active_characters": active_characters
    }

class UserMessageRequest(BaseModel):
    message: str
    target: Optional[str] = None

@app.post("/conversation/user")
def user_message(req: UserMessageRequest, background_tasks: BackgroundTasks):
    global active_characters
    # ★ target 을 무조건 존중한다(자동 대화가 active_characters 를 덮어써도 안 흔들리게).
    #   target 이 없을 때만 기존 active[0] 로 폴백.
    responder = req.target if req.target in DEFAULT_CHARACTERS else (
        active_characters[0] if active_characters else None)
    if responder is None:
        return {"speaker": "", "reply": "(응답할 캐릭터가 없습니다)"}

    # 이후 맥락(call_ollama 의 talk_target 등)을 이 캐릭터-플레이어 1:1 로 고정.
    active_characters = [responder]

    with _conv_lock:
        conversation_history.append({
            "speaker": "플레이어",
            "message": req.message,
            "timestamp": time.time()
        })
        history_snapshot = list(conversation_history)
    reply = call_ollama(responder, history_snapshot, req.message)
    with _conv_lock:
        conversation_history.append({"speaker": responder, "message": reply, "timestamp": time.time()})
    # 대화 내용에 따라 플레이어-캐릭터 호감도를 소폭 조정(응답 뒤 백그라운드).
    background_tasks.add_task(
        judge_and_adjust_relationship, "플레이어", responder,
        f"플레이어: {req.message}\n{responder}: {reply}")
    return {"speaker": responder, "reply": reply}

class GroupMessageRequest(BaseModel):
    message: str
    characters: list[str]   # 응답할 캐릭터들(말하는 순서대로, 최대 3)

@app.post("/conversation/group")
def group_message(req: GroupMessageRequest, background_tasks: BackgroundTasks):
    """플레이어 한 마디에 여러 캐릭터가 '차례로' 반응한다(진짜 3자 대화).
    앞 캐릭터의 대답까지 맥락에 넣어 다음 캐릭터가 이어 말하므로, 서로 주고받는 느낌이 난다."""
    global active_characters
    speakers = [c for c in req.characters if c in DEFAULT_CHARACTERS][:3]
    if not speakers:
        return {"replies": []}

    # call_ollama 가 상대/말투 맥락을 active_characters 로 잡으므로 맞춰 둔다.
    active_characters = speakers

    with _conv_lock:
        conversation_history.append({"speaker": "플레이어", "message": req.message, "timestamp": time.time()})

    replies = []
    for sp in speakers:
        others = [c for c in speakers if c != sp]
        rel_ctx = relationship_description(sp, others[0]) if others else None
        with _conv_lock:
            hist = list(conversation_history)
        # 플레이어 발화와 앞 캐릭터 대답이 이미 history 에 들어 있으므로 user_message 는 비운다.
        line = call_ollama(sp, hist, relationship_ctx=rel_ctx)
        with _conv_lock:
            conversation_history.append({"speaker": sp, "message": line, "timestamp": time.time()})
        replies.append({"speaker": sp, "line": line})

    with _conv_lock:
        while len(conversation_history) > 100:
            conversation_history.pop(0)

    # 플레이어와 각 캐릭터의 호감도를 대화 내용에 따라 조정(응답 뒤 백그라운드).
    for r in replies:
        background_tasks.add_task(
            judge_and_adjust_relationship, "플레이어", r["speaker"],
            f"플레이어: {req.message}\n{r['speaker']}: {r['line']}")

    return {"replies": replies}

class SetCharactersRequest(BaseModel):
    characters: list[str]

@app.post("/characters/set")
def set_characters(req: SetCharactersRequest):
    global active_characters
    valid = [c for c in req.characters if c in DEFAULT_CHARACTERS]
    if len(valid) < 1:
        return {"error": f"유효한 캐릭터 없음. 사용 가능: {list(DEFAULT_CHARACTERS.keys())}"}
    active_characters = valid[:3]
    return {"active_characters": active_characters}

@app.get("/characters")
def get_characters():
    return {"characters": list(DEFAULT_CHARACTERS.values())}

@app.post("/conversation/clear")
def clear_conversation():
    global current_speaker_index
    with _conv_lock:
        conversation_history.clear()
    current_speaker_index = 0
    return {"message": "초기화 완료"}

@app.post("/conversation/next")
def conversation_next():
    global current_speaker_index
    if not active_characters:
        return {"error": "활성 캐릭터 없음"}

    speaker = active_characters[current_speaker_index % len(active_characters)]
    others = [c for c in active_characters if c != speaker]
    rel_ctx = relationship_description(speaker, others[0]) if others else None

    with _conv_lock:
        history_snapshot = list(conversation_history)
    reply = call_ollama(speaker, history_snapshot, relationship_ctx=rel_ctx)
    with _conv_lock:
        conversation_history.append({"speaker": speaker, "message": reply, "timestamp": time.time()})
        if len(conversation_history) > 100:
            conversation_history.pop(0)

    current_speaker_index += 1
    return {"speaker": speaker, "line": reply}

@app.get("/character/line")
def get_character_line(name: Optional[str] = None):
    char_name = name if name in DEFAULT_CHARACTERS else active_characters[0]
    situation = generate_soliloquy_prompt(char_name)
    with _conv_lock:
        recent = list(conversation_history[-6:])
    line = call_ollama(char_name, recent, situation)
    return {"speaker": char_name, "line": line}

class ExchangeRequest(BaseModel):
    char_a: str
    char_b: str
    turns: int = 3

@app.post("/conversation/exchange")
def conversation_exchange(req: ExchangeRequest, background_tasks: BackgroundTasks):
    """두 캐릭터의 짧은 대화 한 묶음(turns턴)을 '한 번에' 생성해 리스트로 반환.
    클라이언트(아래 박스)가 클릭으로 한 줄씩 넘겨 보여주기 위함.
    대사는 서버에서 순차 생성되지만 HTTP 왕복은 1회뿐이라, 플레이어는 한 번만 기다린다.
    char_a가 먼저 말한다(=플레이어가 다가간 캐릭터를 char_a로 넘기면 그 캐릭터가 응답 주체가 됨)."""
    global active_characters, _pending_topic
    a, b = req.char_a, req.char_b
    if a not in DEFAULT_CHARACTERS or b not in DEFAULT_CHARACTERS or a == b:
        return {"error": f"유효하지 않은 캐릭터 쌍: {a}, {b}"}

    active_characters = [a, b]
    turns = max(1, min(req.turns, 6))  # 안전 상한

    # 대화 주제 씨앗(관계·성격 기반). 첫 발화에만 상황으로 주입.
    situation = generate_event_situation(a, b)

    speakers = [a, b]
    with _conv_lock:
        local_history = list(conversation_history[-8:])

    # 플레이어가 방금 꺼낸 화제가 있으면, 이번 대화 첫 발화를 '무조건' 그 주제로 시작시킨다(1회 소비).
    forced_topic = _pending_topic
    _pending_topic = None

    lines = []
    for i in range(turns):
        speaker = speakers[i % 2]
        other = speakers[(i + 1) % 2]
        rel_ctx = relationship_description(speaker, other)
        if i == 0 and forced_topic:
            um = (f"방금 플레이어가 '{forced_topic}'(이)라는 화제를 꺼냈어. "
                  f"너는 그걸 듣고 {other}에게 이 '{forced_topic}'에 대한 네 생각이나 궁금한 점을 "
                  f"네 성격과 말투 그대로 한 문장으로 먼저 건네. 반드시 이 주제에 대한 말이어야 해.")
        elif i == 0:
            um = f"[상황] {situation}"
        else:
            um = None
        line = call_ollama(speaker, local_history, user_message=um, relationship_ctx=rel_ctx)
        local_history.append({"speaker": speaker, "message": line, "timestamp": time.time()})
        lines.append({"speaker": speaker, "line": line})

    # 생성된 대화를 전역 히스토리에 커밋(이후 플레이어 응답의 맥락 유지)
    with _conv_lock:
        for l in lines:
            conversation_history.append({"speaker": l["speaker"], "message": l["line"], "timestamp": time.time()})
        while len(conversation_history) > 100:
            conversation_history.pop(0)

    # 가끔(35%) 마지막 화자가 옆에서 듣던 플레이어를 향해 말을 건넨다.
    # → 클라이언트는 이 한 줄을 자막 끝에 띄우고 "끼어들기" 대신 "대답하기"로 유도한다.
    player_question = ""
    if random.random() < 0.35 and lines:
        asker = lines[-1]["speaker"]
        other = a if asker == b else b
        with _conv_lock:
            hist2 = list(conversation_history[-8:])
        pq_situation = (
            f"방금 {asker}와(과) {other}가 나누던 얘기를 플레이어가 옆에서 듣고 있었다. "
            f"{asker}이(가) 문득 플레이어를 쳐다보며 그 대화에 끌어들이듯 가볍게 말을 걸거나 묻는다. "
            f"플레이어가 대답하고 싶어지게, 딱 한 문장으로."
        )
        player_question = call_ollama(
            asker, hist2, user_message=pq_situation,
            relationship_ctx=relationship_description(asker, other)).strip()
        if player_question:
            with _conv_lock:
                conversation_history.append(
                    {"speaker": asker, "message": player_question, "timestamp": time.time()})

    # 두 캐릭터의 호감도를 이 대화 내용에 따라 조정(응답 뒤 백그라운드).
    convo_text = "\n".join(f"{l['speaker']}: {l['line']}" for l in lines)
    background_tasks.add_task(judge_and_adjust_relationship, a, b, convo_text)

    return {"lines": lines, "situation": situation, "player_question": player_question}

# ========================
# 엔드포인트 — 신규 (관계/이벤트)
# ========================

@app.get("/relationships")
def get_all_relationships():
    """모든 캐릭터 쌍의 관계 반환"""
    with _rel_lock:
        data = _load_data()
    result = {}
    for key, rel in data.get("relationships", {}).items():
        chars = rel.get("chars", key.split("|"))
        result[key] = {
            "chars": chars,
            "intimacy": rel["intimacy"],
            "trust": rel["trust"],
            "emotion": rel["emotion"],
            "milestones": rel.get("milestones", []),
        }
    return {"relationships": result}

@app.get("/relationships/{char_a}/{char_b}")
def get_relationship_pair(char_a: str, char_b: str):
    """특정 두 캐릭터의 관계 반환"""
    rel = get_relationship(char_a, char_b)
    return {
        "chars": [char_a, char_b],
        "intimacy": rel["intimacy"],
        "trust": rel["trust"],
        "emotion": rel["emotion"],
        "milestones": rel.get("milestones", []),
        "description": relationship_description(char_a, char_b),
    }

@app.post("/event/trigger")
def trigger_event(char_a: Optional[str] = None, char_b: Optional[str] = None, turns: int = 3):
    """수동으로 이벤트 강제 실행 (플레이어가 상황을 만들 때)"""
    chars = list(active_characters)
    if len(chars) < 2:
        return {"error": "캐릭터 2명 이상 필요"}

    a = char_a if char_a in chars else chars[0]
    b = char_b if char_b in chars and char_b != a else ([c for c in chars if c != a] or [chars[0]])[0]

    dialogue = run_autonomous_event(a, b, turns=turns)
    rel = get_relationship(a, b)
    return {
        "chars": [a, b],
        "dialogue": dialogue,
        "relationship": {
            "intimacy": rel["intimacy"],
            "trust": rel["trust"],
            "emotion": rel["emotion"],
        }
    }

class TopicRequest(BaseModel):
    text: str

@app.post("/topic/add")
def add_topic(req: TopicRequest):
    """플레이어가 제시한 대화 주제를 저장한다.
    이후 generate_event_situation 이 이 주제를 끌어와 캐릭터들이 관련 대화를 하게 된다."""
    global _pending_topic
    text = (req.text or "").strip()
    if not text:
        return {"ok": False, "reason": "빈 주제"}
    with _rel_lock:
        data = _load_data()
        topics = data.get("topics", [])
        topics.append({"text": text, "from": "플레이어", "timestamp": time.time()})
        if len(topics) > 30:
            topics = topics[-30:]
        data["topics"] = topics
        _save_data(data)
    # 방금 받은 화제는 다음 관전 대화에서 '무조건 한 번' 다루도록 표시한다.
    _pending_topic = text
    return {"ok": True, "count": len(topics)}

@app.get("/topics")
def get_topics(limit: int = 10):
    with _rel_lock:
        data = _load_data()
    return {"topics": data.get("topics", [])[-limit:]}

@app.get("/events")
def get_events(limit: int = 10):
    """최근 이벤트 로그 반환"""
    with _rel_lock:
        data = _load_data()
    events = data.get("events", [])
    return {"events": events[-limit:]}

# ========================
# 플레이어 편의 엔드포인트
# ========================

@app.post("/player/approach")
def player_approach(name: str):
    """플레이어가 캐릭터에게 다가갔을 때 자연스러운 첫 반응 생성.
    랜덤 혼잣말이 아니라, 지금 뭘 하다가 플레이어를 발견한 건지 맥락 반영."""
    char = DEFAULT_CHARACTERS.get(name)
    if not char:
        return {"error": f"캐릭터 없음: {name}"}

    # 이 캐릭터의 최근 대화/행동 맥락
    with _conv_lock:
        recent = [h for h in conversation_history[-8:] if h["speaker"] == name]
    recent_ctx = "\n".join(f"{h['message']}" for h in recent[-3:]) if recent else ""

    # 최근 관계된 이벤트
    with _rel_lock:
        data = _load_data()
    my_events = [e for e in data.get("events", [])[-5:] if name in e.get("chars", [])]
    event_ctx = ""
    if my_events:
        last = my_events[-1]
        summary = last.get("result", {}).get("summary", "")
        other = [c for c in last.get("chars", []) if c != name]
        if summary and other:
            event_ctx = f"방금 전 {other[0]}와(과) 있었던 일: {summary}"

    situation = f"""플레이어가 갑자기 {name}에게 다가왔다.
{name}은(는) 지금 혼자 있다가 플레이어를 발견한 상황이다.
{'최근 {name}이(가) 한 말: ' + recent_ctx if recent_ctx else ''}
{event_ctx}

이 상황에서 {name}이(가) 플레이어를 알아보고 자연스럽게 반응하는 첫 한마디를 해."""

    with _conv_lock:
        recent_hist = list(conversation_history[-6:])
    line = call_ollama(name, recent_hist, user_message=situation)

    # 대화 히스토리에 남겨서 맥락 유지
    with _conv_lock:
        conversation_history.append({
            "speaker": name,
            "message": line,
            "timestamp": time.time()
        })

    return {"speaker": name, "line": line}


@app.get("/conversation/status")
def conversation_status():
    """Unity가 polling해서 현재 AI 대화 상황을 파악하는 엔드포인트.
    화면 HUD에 '세비-호 ↔ 류 대화 중' 같은 알림을 띄울 때 사용."""
    is_active = len(active_characters) >= 2

    # 최근 30초 이내 메시지가 있으면 "대화 중"으로 판단
    now = time.time()
    with _conv_lock:
        recent = [h for h in conversation_history[-6:] if now - h.get("timestamp", 0) < 30]

    active_speakers = list({h["speaker"] for h in recent if h["speaker"] != "플레이어" and h["speaker"] != "상황"})

    latest = None
    if recent:
        for h in reversed(recent):
            if h["speaker"] not in ("플레이어", "상황"):
                latest = {"speaker": h["speaker"], "message": h["message"]}
                break

    return {
        "is_talking": len(active_speakers) >= 2,
        "speakers": active_speakers,
        "latest_line": latest,
        "recent_lines": [
            {"speaker": h["speaker"], "message": h["message"]}
            for h in recent
            if h["speaker"] not in ("상황",)
        ][-4:],
    }


# ========================
# 카드게임 결과 → 관계 반영
# ========================

class GameResultRequest(BaseModel):
    winner: str                          # "플레이어" or AI 이름
    player_rank: int = 1                 # 플레이어 순위 (1=1등, 4=꼴등)
    total_players: int = 4
    pass_count: int = 0                  # 플레이어의 패스 횟수

@app.post("/game/result")
def report_game_result(req: GameResultRequest):
    """카드게임 종료 후 결과를 받아 모든 캐릭터와의 관계에 반영.
    승리하면 캐릭터들이 플레이어를 인정하는 방향, 패배하면 약간의 동정/도발."""

    chars = list(DEFAULT_CHARACTERS.keys())
    player_won = req.winner == "플레이어"

    # 순위 기반 관계 변화량 결정
    if req.player_rank == 1:
        base_intimacy, base_trust = 5, 3
        result_desc = "플레이어가 카드게임에서 1등으로 승리했다"
    elif req.player_rank == 2:
        base_intimacy, base_trust = 2, 1
        result_desc = "플레이어가 카드게임에서 2등을 했다"
    elif req.player_rank == 3:
        base_intimacy, base_trust = -1, 0
        result_desc = "플레이어가 카드게임에서 3등을 했다"
    else:
        base_intimacy, base_trust = -3, -1
        result_desc = "플레이어가 카드게임에서 꼴등을 했다"

    # 패스가 많으면 신뢰 감소 (소극적 플레이)
    if req.pass_count >= 3:
        base_trust -= 2

    # 각 캐릭터와의 관계 업데이트
    reactions = {}
    for char_name in chars:
        update_relationship("플레이어", char_name, base_intimacy, base_trust, "neutral", result_desc)
        rel = get_relationship("플레이어", char_name)

        # 캐릭터 성격에 맞는 짧은 반응 생성
        char_info = DEFAULT_CHARACTERS[char_name]
        reaction_prompt = f"""너는 '{char_name}'이야.
[성격] {char_info['personality']}
[말투] {char_info['speech_style']}

방금 플레이어가 카드게임에서 {req.player_rank}등을 했어.
현재 {char_name}과 플레이어의 관계: 친밀도 {rel['intimacy']:+d}, 신뢰 {rel['trust']}/100

이 결과에 대해 {char_name}이(가) 할 법한 짧은 한마디를 해. (1문장, 존댓말 ~요 체)"""

        reaction = call_ollama_judge(reaction_prompt)
        if not reaction:
            if char_name == "세비-호":
                reaction = "흥미롭네요."
            elif char_name == "진":
                reaction = "그렇게 될 줄 알았어요."
            else:
                reaction = "그렇군요."
        reactions[char_name] = reaction

    # 이벤트 로그에 게임 결과 기록
    with _rel_lock:
        data = _load_data()
        data["events"].append({
            "situation": result_desc,
            "chars": ["플레이어"] + chars,
            "dialogue": [{"speaker": k, "message": v, "timestamp": time.time()} for k, v in reactions.items()],
            "result": {
                "type": "card_game",
                "winner": req.winner,
                "player_rank": req.player_rank,
                "summary": result_desc,
            },
            "timestamp": time.time()
        })
        if len(data["events"]) > 50:
            data["events"] = data["events"][-50:]
        _save_data(data)

    return {
        "result": result_desc,
        "reactions": reactions,
        "relationship_changes": {
            "intimacy": base_intimacy,
            "trust": base_trust,
        }
    }


# ========================
# 마이룸 대시보드
# ========================

@app.get("/myroom/status")
def myroom_status():
    """마이룸 화면에서 사용할 종합 상태 반환:
    - 각 캐릭터와 플레이어의 관계 (친밀도, 신뢰, 감정)
    - 캐릭터 간 관계
    - 최근 이벤트 요약
    """
    chars = list(DEFAULT_CHARACTERS.keys())

    # 플레이어 ↔ 각 캐릭터 관계
    player_relations = {}
    for c in chars:
        rel = get_relationship("플레이어", c)
        player_relations[c] = {
            "intimacy": rel["intimacy"],
            "trust": rel["trust"],
            "emotion": rel["emotion"],
            "milestones": rel.get("milestones", [])[-3:],  # 최근 3개만
        }

    # 캐릭터 간 관계 (2명일 때는 1쌍)
    char_relations = {}
    for i in range(len(chars)):
        for j in range(i + 1, len(chars)):
            rel = get_relationship(chars[i], chars[j])
            key = f"{chars[i]}|{chars[j]}"
            char_relations[key] = {
                "intimacy": rel["intimacy"],
                "trust": rel["trust"],
                "emotion": rel["emotion"],
            }

    # 최근 이벤트 (최대 5개, 요약만)
    with _rel_lock:
        data = _load_data()
    events = data.get("events", [])[-5:]
    event_summaries = []
    for ev in events:
        summary = {
            "situation": ev.get("situation", ""),
            "chars": ev.get("chars", []),
            "timestamp": ev.get("timestamp", 0),
        }
        # 카드게임 결과인 경우 타입 표시
        result = ev.get("result", {})
        if result and result.get("type") == "card_game":
            summary["type"] = "card_game"
            summary["game_result"] = result.get("summary", "")
        else:
            summary["type"] = "conversation"
        event_summaries.append(summary)

    # 캐릭터 프로필 정보
    profiles = {}
    for name, info in DEFAULT_CHARACTERS.items():
        profiles[name] = {
            "personality": info["personality"],
            "speech_style": info["speech_style"],
        }

    # 플레이어가 저장한 대화 주제 (최근 것부터)
    topics_list = data.get("topics", [])
    recent_topics = [
        {"text": t.get("text", ""), "from": t.get("from", ""), "timestamp": t.get("timestamp", 0)}
        for t in topics_list[-10:]
    ]

    return {
        "characters": profiles,
        "player_relations": player_relations,
        "char_relations": char_relations,
        "recent_events": event_summaries,
        "topics": recent_topics,
    }
