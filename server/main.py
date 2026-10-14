from fastapi import FastAPI
from pydantic import BaseModel
from typing import Optional
from contextlib import asynccontextmanager, contextmanager
import requests
import time
import threading
import queue
import json
import os
import random


@asynccontextmanager
async def _lifespan(app):
    # 서버가 뜰 때 백그라운드 작업자(관계 판정·혼잣말 미리 만들기)를 시작한다.
    # import 시점이 아니라 여기서 시작하므로 bench_models.py 가 main 을 import 해도 스레드가 안 뜬다.
    _start_background_worker()
    yield


app = FastAPI(lifespan=_lifespan)

# ========================
# LLM 설정 — 모델 교체는 여기(또는 환경변수)만 바꾸면 된다
# ========================
def _default_ollama_url() -> str:
    """Ollama 주소. OLLAMA_URL > OLLAMA_HOST(Ollama 자체 설정, 예: 0.0.0.0:11434) > 127.0.0.1:11434.
    'localhost' 대신 127.0.0.1 을 쓴다(Windows에서 IPv6로 먼저 붙으려다 실패·지연되는 경우 방지)."""
    if os.getenv("OLLAMA_URL"):
        return os.getenv("OLLAMA_URL").rstrip("/")
    host = os.getenv("OLLAMA_HOST", "").strip()
    if host:
        if "://" not in host:
            host = "http://" + host
        host = host.replace("0.0.0.0", "127.0.0.1").rstrip("/")
        if host.count(":") < 2:
            host += ":11434"
        return host
    return "http://127.0.0.1:11434"


OLLAMA_URL = _default_ollama_url()
OLLAMA_CHAT_URL = f"{OLLAMA_URL}/api/chat"
# 시스템에 프록시(HTTP_PROXY 등)가 잡혀 있어도 로컬 Ollama 호출은 프록시를 거치지 않게 한다.
for _k in ("NO_PROXY", "no_proxy"):
    os.environ[_k] = ",".join(x for x in (os.environ.get(_k, ""), "localhost", "127.0.0.1") if x)

# 캐릭터 대사용 / 내부 판정용 모델.
# 둘을 같은 모델로 두면 메모리에 모델이 하나만 올라가서 전환 지연과 메모리 부담이 사라진다.
# 어떤 모델이 좋은지는 bench_models.py 를 돌려서 고른다.
# 2026-10-15 벤치마크(Core Ultra 5 125H, CPU): qwen3.5:4b + 압축 프롬프트가
#   캐릭터 말투·존댓말이 가장 좋고 대사 1줄 약 4초, NPC 대화 묶음 약 15초(예전 7.8B 방식 약 55초).
#   더 빠른 쪽이 필요하면 exaone3.5:2.4b(대사 약 3초, 묶음 약 9초, 말투는 덜 또렷함).
MODEL_NAME  = os.getenv("SEVENS_MODEL", "qwen3.5:4b")
JUDGE_MODEL = os.getenv("SEVENS_JUDGE_MODEL", "qwen3.5:4b")   # 같은 모델 → 메모리에 하나만 상주

# 컨텍스트 길이. 프롬프트가 1~2천 토큰이라 4096이면 충분하다.
# 지정하지 않으면 최신 Ollama는 모델에 따라 훨씬 큰 값을 잡아 메모리·속도를 낭비한다.
NUM_CTX = int(os.getenv("SEVENS_NUM_CTX", "4096"))

# 빠른 모드(기본 켜짐). 끄면(SEVENS_FAST=0) 예전 방식으로 동작한다.
#   - NPC 대화 묶음(/conversation/exchange): 상황+대사 N줄+관계 판정을 LLM 1회(JSON)로 생성
#     (예전: 상황 1회 + 대사 N회 + 판정 1회 = 5~6회)
#   - 혼잣말(/character/line): 상황 생성 호출 없이 1회로 생성
#   - 게임 결과 반응(/game/result): 캐릭터 3명 반응을 1회로 생성
FAST_MODE = os.getenv("SEVENS_FAST", "1") == "1"

# 프롬프트 방식.
#   compact(기본): 짧은 공통 규칙 + 캐릭터 카드(정체·말버릇·관계·예시) + 관계 한 줄 + 최근 대화 4줄
#                  → 프롬프트 토큰이 약 1/3, 응답이 빨라지고 캐릭터 말투가 더 또렷해진다
#   full         : 예전의 긴 규칙·성격·말투 설명
PROMPT_STYLE = os.getenv("SEVENS_PROMPT", "compact")

# 혼잣말 미리 만들어 두기. Unity가 /character/line 을 부르고 있는 동안에만 돌고,
# 플레이어가 대화 중이면 즉시 양보(생성 중단)한다.
PREGEN_SOLILOQUY = os.getenv("SEVENS_PREGEN", "1") == "1"

# 모델을 메모리에 계속 상주시켜 호출 때마다 다시 로드하는 지연을 제거.
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
# 압축 프롬프트(PROMPT_STYLE="compact")용 캐릭터 카드
#   설명을 길게 늘어놓는 대신 '정체 / 말버릇 / 주변 사람 / 예시 대사' 네 줄로 캐릭터를 박아 넣는다.
#   작은 모델일수록 긴 설명보다 짧고 또렷한 예시를 더 잘 따라 한다.
# ========================
COMPACT_RULES = """너는 섬에 사는 캐릭터다. AI 비서가 아니다.
규칙: 한국어 구어체 존댓말(~요/~죠)로 딱 한 문장. 설명·비서 말투·이름표 금지. 유저는 '플레이어'라고 부른다.
예시 대사는 말투 참고용이다. 그대로 따라 하지 말고 상황에 맞게 새로 말한다."""

CHARACTER_CARDS = {
    "세비-호": {
        "core": "자신만만한 승부사. 지는 걸 죽기보다 싫어하고, 걱정도 허세로 감춘다.",
        "voice": "짧고 단정하게. 가끔 자기 자랑을 슬쩍 붙이고, 남 칭찬은 비꼬듯이 한다.",
        "others": "류는 얄미운 라이벌, 진은 속을 모를 사람. 플레이어는 은근히 신경 쓰이지만 티 안 낸다.",
        "examples": ["이길 줄 알았어요. 저니까요.", "걱정한 거 아니에요. 그냥 확인한 거죠.",
                     "운이 좋았네요. 다음엔 어림없어요."],
    },
    "류": {
        "core": "말수 적은 관찰자. 감정은 숨기고 핵심만 찌른다.",
        "voice": "열 글자 안팎의 건조한 한마디. 감탄사·느낌표는 안 쓴다.",
        "others": "세비-호의 허세는 꿰뚫어 보지만 굳이 반박 안 한다. 진과는 말없이 통한다.",
        "examples": ["그럴 줄 알았어요.", "보이는 게 다는 아니죠.", "늦었네요."],
    },
    "진": {
        "core": "속을 알 수 없는 예언가 같은 사람. 비밀이 많아 보인다.",
        "voice": "나직하게 말끝을 흐린다(…). 직답 대신 질문이나 비유로 돌려준다.",
        "others": "세비-호의 허세를 재밌어하고 류의 침묵을 존중한다. 플레이어에게 묘하게 관심 있다(이유는 비밀).",
        "examples": ["재밌는 선택이네요… 이유는 곧 알게 되겠죠.", "그 답, 이미 알고 있지 않아요?",
                     "오늘은 바람이 말이 많네요…"],
    },
}


def character_card(name: str) -> str:
    """캐릭터 카드 4줄. 카드가 없는 캐릭터는 예전 성격·말투 설명을 줄여서 쓴다."""
    c = CHARACTER_CARDS.get(name)
    if not c:
        info = DEFAULT_CHARACTERS.get(name, {})
        return f"[{name}]\n{info.get('personality', '')[:120]}\n{info.get('speech_style', '')[:120]}"
    ex = " / ".join(f'"{e}"' for e in c["examples"])
    return (f"[{name}]\n정체: {c['core']}\n말버릇: {c['voice']}\n"
            f"주변: {c['others']}\n예: {ex}")


_CLOSENESS = [(60, "아주 친함"), (20, "조금 친함"), (-20, "어색함"), (-60, "껄끄러움")]
_EMOTION_KO = {"friendly": "호감", "neutral": "담담", "curious": "궁금", "tense": "긴장", "hostile": "적대"}


def compact_relation(rel: dict) -> str:
    """관계를 숫자 없이 한 줄로: '조금 친함·호감 (최근: 카드게임 역전승)'."""
    intimacy = rel.get("intimacy", 0)
    closeness = next((w for th, w in _CLOSENESS if intimacy >= th), "서로 싫어함")
    text = f"{closeness}·{_EMOTION_KO.get(rel.get('emotion', 'neutral'), '담담')}"
    ms = rel.get("milestones") or []
    if ms:
        text += f" (최근: {ms[-1]['text'][:20]})"
    return text


# ========================
# 관계 시스템
# ========================
_rel_lock = threading.Lock()

def _rel_key(a: str, b: str) -> str:
    """항상 알파벳 순으로 정렬해 동일한 키 보장"""
    return f"{min(a,b)}|{max(a,b)}"

def _load_data() -> dict:
    data = {}
    if os.path.exists(DATA_PATH):
        try:
            with open(DATA_PATH, "r", encoding="utf-8") as f:
                data = json.load(f) or {}
        except Exception:
            data = {}
    data.setdefault("relationships", {})
    data.setdefault("events", [])
    return data

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
    """LLM 프롬프트에 삽입할 관계 요약 (압축 모드면 한 줄)"""
    rel = get_relationship(a, b)
    if PROMPT_STYLE == "compact":
        return compact_relation(rel)
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

# ========================
# LLM 호출 계층
#   - 모델 기능(thinking 여부) 자동 감지 → 생각 모드 끔
#   - 플레이어 상호작용(전경) 우선, 백그라운드 작업은 양보
# ========================
class Preempted(BaseException):
    """백그라운드 생성 도중 플레이어 요청이 들어와 양보(중단)했음을 알림.
    일반 `except Exception` 에 삼켜지지 않도록 BaseException 을 상속한다."""


_tls = threading.local()            # _tls.background=True 인 스레드의 호출은 양보 가능
_caps_cache: dict = {}
_caps_lock = threading.Lock()


def _model_caps(model: str) -> set:
    """Ollama /api/show 의 capabilities(예: completion, thinking, vision). 성공한 결과만 캐시."""
    with _caps_lock:
        if model in _caps_cache:
            return _caps_cache[model]
    try:
        r = requests.post(f"{OLLAMA_URL}/api/show", json={"model": model}, timeout=10)
        if r.status_code == 200:
            caps = set(r.json().get("capabilities") or [])
            with _caps_lock:
                _caps_cache[model] = caps
            return caps
    except Exception:
        pass
    return set()


def _model_options(model: str, temperature: float, num_predict: int) -> dict:
    opts = {"temperature": temperature, "num_predict": num_predict, "num_ctx": NUM_CTX}
    low = model.lower()
    # EXAONE 4.0 은 비추론 모드에서 temperature 0.6 이하를 권장(한국어 품질).
    if "exaone-4" in low or "exaone4" in low:
        opts["temperature"] = min(temperature, 0.6)
    return opts


class _Priority:
    """전경(플레이어가 기다리는) 요청 수와 마지막 시각을 추적한다."""
    def __init__(self):
        self.lock = threading.Lock()
        self.active = 0
        self.last = 0.0

    def busy(self, quiet: float = 1.5) -> bool:
        with self.lock:
            return self.active > 0 or (time.time() - self.last) < quiet


_prio = _Priority()


@contextmanager
def interactive():
    """플레이어가 응답을 기다리는 구간. 이 안에서는 백그라운드 생성이 즉시 양보한다."""
    with _prio.lock:
        _prio.active += 1
        _prio.last = time.time()
    try:
        yield
    finally:
        with _prio.lock:
            _prio.active -= 1
            _prio.last = time.time()
        # 대화로 맥락이 바뀌었으니 미리 만들어 둔 혼잣말은 버린다.
        with _pool_lock:
            _soliloquy_pool.clear()


# 호출 통계(/llm/status 에서 확인)
_stats_lock = threading.Lock()
_stats: dict = {}


def _record(kind: str, seconds: float):
    with _stats_lock:
        s = _stats.setdefault(kind, {"calls": 0, "recent": []})
        s["calls"] += 1
        s["recent"] = (s["recent"] + [round(seconds, 2)])[-30:]


_THINK_RE = _re.compile(r"<think>.*?</think>", _re.S)


def ollama_chat(model: str, messages: list, *, temperature: float, num_predict: int,
                fmt=None, kind: str = "chat", background: Optional[bool] = None) -> str:
    """Ollama /api/chat 호출 공통 함수. 텍스트(content)를 돌려준다.
    background=True(또는 백그라운드 스레드)면 스트리밍으로 받으면서,
    플레이어 요청이 들어오는 순간 연결을 끊어 생성을 중단하고 Preempted 를 던진다."""
    if background is None:
        background = getattr(_tls, "background", False)
    payload = {
        "model": model,
        "messages": messages,
        "stream": background,
        "keep_alive": OLLAMA_KEEP_ALIVE,
        "options": _model_options(model, temperature, num_predict),
    }
    if "thinking" in _model_caps(model):
        payload["think"] = False            # 생각(추론) 모드 끔 — 대사에는 필요 없고 느리기만 함
    if fmt is not None:
        payload["format"] = fmt             # JSON 스키마(구조화 출력)

    t0 = time.time()
    if not background:
        res = requests.post(OLLAMA_CHAT_URL, json=payload, timeout=240)
        data = res.json()
        if "error" in data:
            raise RuntimeError(data["error"])
        content = data.get("message", {}).get("content", "")
    else:
        if _prio.active > 0:
            raise Preempted()
        parts = []
        done = threading.Event()
        preempted = threading.Event()
        res = requests.post(OLLAMA_CHAT_URL, json=payload, stream=True, timeout=240)

        def _watch():
            # 프롬프트 처리 중(아직 토큰이 안 나올 때)에도 끊을 수 있도록 별도 감시.
            while not done.wait(0.1):
                if _prio.active > 0:
                    preempted.set()
                    try:
                        res.close()             # 연결을 끊으면 Ollama도 생성을 멈춘다
                    except Exception:
                        pass
                    return

        threading.Thread(target=_watch, daemon=True).start()
        try:
            for raw in res.iter_lines():
                if preempted.is_set():
                    break
                if not raw:
                    continue
                chunk = json.loads(raw)
                if "error" in chunk:
                    raise RuntimeError(chunk["error"])
                parts.append(chunk.get("message", {}).get("content", ""))
                if chunk.get("done"):
                    break
        except Exception:
            if not preempted.is_set():
                raise
        finally:
            done.set()
            res.close()
        if preempted.is_set():
            raise Preempted()
        content = "".join(parts)

    _record(kind, time.time() - t0)
    return _THINK_RE.sub("", content).strip()


# ── 존댓말 보정 ─────────────────────────────────────────────
# 작은 모델은 프롬프트에 '존댓말'을 적어도 종종 반말로 끝낸다(벤치마크에서 EXAONE 2.4B는 80%가 반말).
# 문장 끝 어미만 보고 흔한 반말 어미를 존댓말로 바꾼다. 확실한 경우만 바꾸고, 애매하면 그대로 둔다.
_POLITE_DONE = ("요", "죠", "니까")
_FORMAL_REPLACE = [          # 번역투 ~습니다/~입니다 → 구어체 ~어요 (흔한 것만)
    ("있습니다", "있어요"), ("없습니다", "없어요"), ("었습니다", "었어요"), ("았습니다", "았어요"),
    ("겠습니다", "겠어요"), ("했습니다", "했어요"), ("합니다", "해요"), ("됩니다", "돼요"),
]
_POLITE_REPLACE = [          # (반말 끝, 존댓말 끝) — 긴 것부터 검사
    ("이야", "이에요"), ("거야", "거예요"), ("게야", "거예요"), ("구나", "군요"),
    ("겠지", "겠죠"), ("잖아", "잖아요"), ("거든", "거든요"),
    ("걱정 마", "걱정 마세요"), ("하지 마", "하지 마세요"),
]
_POLITE_ADD_YO = set("어아해워와봐줘돼겨져려쳐서가내래게데걸까네군고든")   # 끝에 '요'만 붙이면 되는 어미
_HANGUL_RE = _re.compile(r"[\uac00-\ud7a3]")
_NAMES_FOR_VOCATIVE = ("류", "진", "세비-호", "세비호")
_VOCATIVE_TAIL = _re.compile(r"[,\s]+(플레이어|류|진|세비-호)(님|씨|야|아)?$")


def _has_batchim(ch: str) -> bool:
    return "\uac00" <= ch <= "\ud7a3" and (ord(ch) - 0xAC00) % 28 != 0


def _politefy_sentence(body: str) -> str:
    core = body.rstrip()
    tail_ws = body[len(core):]
    for old, new in _FORMAL_REPLACE:
        if core.endswith(old):
            return core[: -len(old)] + new + tail_ws
    for old, new in (("것입니다", "거예요"), ("뿐입니다", "뿐이에요"), ("때문입니다", "때문이에요")):
        if core.endswith(old):                       # '움직입니다'처럼 동사일 수 있는 일반 '~입니다'는 건드리지 않음
            return core[: -len(old)] + new + tail_ws
    if len(_HANGUL_RE.findall(core)) < 3 or core.endswith(_POLITE_DONE) or core.endswith("니다"):
        return body
    m = _VOCATIVE_TAIL.search(core)                  # "축하해, 플레이어" → 호칭 앞부분만 보정
    if m and m.start() > 0:
        return _politefy_sentence(core[: m.start()]) + m.group(0) + tail_ws
    for vocative in _NAMES_FOR_VOCATIVE:            # "또 보네, 류야" 같은 끝 호칭은 건드리지 않음
        if core.endswith((vocative + "야", vocative + "아")):
            return body
    for old, new in _POLITE_REPLACE:
        if core.endswith(old):
            return core[: -len(old)] + new + tail_ws
    last = core[-1]
    if last == "지":                               # 하지 → 하죠, 말이지 → 말이죠
        return core[:-1] + "죠" + tail_ws
    if last == "자":                               # 하자 → 하죠
        return core[:-1] + "죠" + tail_ws
    if last == "야" and len(core) >= 2 and not _has_batchim(core[-2]):   # 뭐야 → 뭐예요
        return core[:-1] + "예요" + tail_ws
    if last == "나" and len(core) >= 2:            # 않나? → 않나요?
        return core + "요" + tail_ws
    if last in _POLITE_ADD_YO:
        return core + "요" + tail_ws
    return body


def politefy(text: str) -> str:
    """문장마다 끝 어미를 보고 반말이면 존댓말로 바꾼다."""
    parts = _re.split(r"([.!?…~]+\s*)", text or "")
    out = []
    for i in range(0, len(parts), 2):
        body = parts[i]
        punct = parts[i + 1] if i + 1 < len(parts) else ""
        out.append(_politefy_sentence(body) + punct if body.strip() else body + punct)
    return "".join(out)


_HAN_RE = _re.compile(r"[\u4e00-\u9fff]+")
_EMOJI_RE = _re.compile("[\U0001F300-\U0001FAFF\u2600-\u27BF\uFE0F]+")
_ASIDE_RE = _re.compile(r"\s*[(（][^)）]*[)）]")


def clean_line(reply: str) -> str:
    """모델 출력 한 줄을 대사로 다듬는다: 이름 라벨·{이름}·괄호 지문·한자·비서 군더더기·겹친 '요요'·
    감싼 따옴표 제거, 최대 2문장, 반말 어미는 존댓말로 보정."""
    reply = (reply or "").strip()
    for char_name in list(DEFAULT_CHARACTERS.keys()) + ["유저", "플레이어"]:
        for prefix in [f"{char_name}: ", f"[{char_name}] ", f"{{{char_name}}} ", f"{{{char_name}}}", f"{char_name}:"]:
            if reply.startswith(prefix):
                reply = reply[len(prefix):].strip()
    if reply in DEFAULT_CHARACTERS:                  # 이름만 덩그러니 나온 줄
        return ""
    reply = _ASIDE_RE.sub("", reply)                  # (웃으며) 같은 괄호 지문
    reply = _HAN_RE.sub("", reply)                    # 중국어(한자) 혼입
    reply = _EMOJI_RE.sub("", reply).replace("  ", " ")   # 이모지
    reply = _strip_quotes(reply)
    # 최대 2문장으로 강제(너무 길게 말하는 경향 차단). 비서 군더더기도 제거.
    reply = _trim_to_sentences(reply, max_sentences=2)
    # 종결어미가 '요요'로 겹쳐 나오는 버릇을 '요'로 정리.
    reply = _fix_double_yo(reply)
    # 두 문장인데 너무 길면(80자 초과) 첫 문장만 — 작은 모델이 설명을 늘어놓는 경우
    if len(reply) > 80:
        first = _re.split(r'(?<=[.!?。…])\s+', reply, maxsplit=1)[0]
        if 0 < len(first) < len(reply):
            reply = first
    # 반말 어미 → 존댓말
    reply = politefy(reply)
    # 대사를 통째로 감싸거나 끝에 흘린 따옴표 제거.
    reply = _strip_quotes(reply)
    return reply.strip()


def build_character_messages(speaker_name: str, history: list, user_message: Optional[str] = None,
                             relationship_ctx: Optional[str] = None,
                             talk_target: Optional[str] = None) -> list:
    """캐릭터 한 명의 대사를 만들기 위한 messages. (bench_models.py 도 이 함수를 그대로 쓴다)"""
    char = DEFAULT_CHARACTERS[speaker_name]
    if talk_target is None:
        others = [c for c in active_characters if c != speaker_name]
        talk_target = ", ".join(others) if others else "플레이어(유저)"

    if PROMPT_STYLE == "compact":
        # 고정 부분(규칙 → 카드)을 앞에, 바뀌는 부분(상대·관계·최근 대화)을 뒤에 둬서
        # 같은 캐릭터의 연속 호출은 Ollama 프롬프트 캐시가 앞부분을 재사용한다.
        system_prompt = f"{COMPACT_RULES}\n\n{character_card(speaker_name)}\n\n너는 '{speaker_name}'다. 지금 상대: {talk_target}"
        if relationship_ctx:
            system_prompt += f"\n관계: {relationship_ctx}"
        messages = [{"role": "system", "content": system_prompt}]
        for h in history[-4:]:
            if h["speaker"] == speaker_name:
                messages.append({"role": "assistant", "content": h["message"][:80]})
            else:
                messages.append({"role": "user", "content": f"{h['speaker']}: {h['message'][:80]}"})
        if user_message:
            messages.append({"role": "user", "content": user_message})
        if messages[-1]["role"] == "user":
            messages[-1] = {"role": "user", "content": messages[-1]["content"] + "\n(존댓말 한 문장으로)"}
        else:
            messages.append({"role": "user", "content": "(이어서 존댓말 한 문장으로)"})
        return messages

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
    return messages


def call_ollama(speaker_name: str, history: list, user_message: Optional[str] = None,
                relationship_ctx: Optional[str] = None) -> str:
    if speaker_name not in DEFAULT_CHARACTERS:
        return "(알 수 없는 캐릭터)"
    messages = build_character_messages(speaker_name, history, user_message, relationship_ctx)
    try:
        # num_predict는 상한선(모델은 끝나면 스스로 멈춤).
        # 너무 낮으면 문장이 끝나기 전에 잘리므로, 두 문장이 완성될 만큼 여유를 둔다.
        reply = ollama_chat(MODEL_NAME, messages, temperature=0.85,
                            num_predict=64 if PROMPT_STYLE == "compact" else 128, kind="dialogue")
        return clean_line(reply)
    except Preempted:
        raise
    except Exception as e:
        return f"(응답 오류: {e})"


def call_ollama_judge(prompt: str, fmt=None) -> str:
    """관계 변화 판정 등 단순 판단용 Ollama 호출"""
    try:
        return ollama_chat(JUDGE_MODEL, [{"role": "user", "content": prompt}],
                           temperature=0.3, num_predict=60, fmt=fmt, kind="judge")
    except Preempted:
        raise
    except Exception:
        return ""

_EMOTIONS = {"friendly", "neutral", "curious", "tense", "hostile"}
JUDGE_SCHEMA = {"type": "object",
                "properties": {"delta": {"type": "integer"},
                               "emotion": {"type": "string", "enum": sorted(_EMOTIONS)}},
                "required": ["delta", "emotion"]}

def judge_and_adjust_relationship(a: str, b: str, convo_text: str):
    """대화 내용을 보고 두 사람의 친밀도를 소폭 조정한다(백그라운드 작업자가 응답 뒤에 실행).
    따뜻·친근하면 +, 평범·무관심하면 0, 차갑거나 다투면 -. '점점' 변하도록 한 번에 최대 ±3."""
    if a == b or not (convo_text or "").strip():
        return
    prompt = (
        f"다음은 '{a}'와(과) '{b}'의 대화야. 이 대화로 둘 사이가 어떻게 변했는지 판단해.\n\n"
        f"[대화]\n{convo_text}\n\n"
        "친밀도 변화량을 -3~+3 정수로 정해(따뜻·친근하면 +, 평범·무관심하면 0, 차갑거나 다투면 -). "
        "감정은 friendly/neutral/curious/tense/hostile 중 하나.\n"
        "delta 와 emotion 으로 답해."
    )
    try:
        out = call_ollama_judge(prompt, fmt=JUDGE_SCHEMA)
        try:
            j = json.loads(out)
            delta = max(-3, min(3, int(j.get("delta", 0))))
            emotion = str(j.get("emotion", "neutral")).lower()
        except Exception:                            # 구조화 출력을 못 쓰는 환경이면 예전 형식으로 해석
            m = _re.search(r"delta\s*=\s*([+-]?\d+)", out)
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
def _world_context(char_a: str, char_b: str) -> tuple:
    """두 캐릭터 대화에 넣을 맥락: (관계 설명, 최근 흐름, 플레이어가 꺼낸 화제)."""
    rel_desc = relationship_description(char_a, char_b)

    # 최근 이벤트 가져오기 (맥락용)
    with _rel_lock:
        data = _load_data()
    recent_events = data.get("events", [])
    recent_summaries = [
        e["result"].get("summary", "")
        for e in recent_events[-3:]
        if (e.get("result") or {}).get("summary")
    ]
    recent_ctx = ("최근 있었던 일: " + " / ".join(recent_summaries)) if recent_summaries else "아직 별다른 일 없음."

    # 플레이어가 꺼낸 최근 화제 — 있으면 캐릭터들이 그 주제로 이야기하게 유도한다.
    topics = data.get("topics", [])
    recent_topics = [t.get("text", "") for t in topics[-3:] if t.get("text")]
    topic_ctx = ("요즘 플레이어가 꺼낸 화제: " + " / ".join(recent_topics)) if recent_topics else "아직 없음."
    return rel_desc, recent_ctx, topic_ctx


def generate_event_situation(char_a: str, char_b: str) -> str:
    """현재 관계 상태와 캐릭터 성격을 바탕으로 LLM이 상황을 직접 생성"""
    rel_desc, recent_ctx, topic_ctx = _world_context(char_a, char_b)
    result = call_ollama_judge(build_situation_prompt(char_a, char_b, rel_desc, recent_ctx, topic_ctx)).strip()

    # fallback: LLM이 빈 결과를 내면 기본값
    if not result:
        result = f"{char_a}와 {char_b}가 섬에서 마주쳤다."
    return result


def build_situation_prompt(char_a: str, char_b: str, rel_desc: str, recent_ctx: str, topic_ctx: str) -> str:
    """(예전 방식) NPC 대화의 상황을 만드는 판정 모델용 프롬프트."""
    char_a_info = DEFAULT_CHARACTERS.get(char_a, {})
    char_b_info = DEFAULT_CHARACTERS.get(char_b, {})
    return f"""너는 AI 캐릭터 시뮬레이션의 시나리오 작가야.
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

# ========================
# 빠른 모드(FAST_MODE): LLM 호출 횟수 줄이기
# ========================
_SOLILOQUY_SEEDS = [
    "방금 문득 떠오른 생각", "지금 눈앞의 풍경", "요즘 카드게임", "다른 캐릭터들",
    "플레이어", "심심함", "오늘 기분", "섬의 날씨", "최근에 들은 소문",
]


def _soliloquy_recent_ctx(char_name: str) -> str:
    with _rel_lock:
        data = _load_data()
    my_events = [e for e in data.get("events", [])[-5:] if char_name in e.get("chars", [])]
    if my_events:
        last = my_events[-1]
        summary = (last.get("result") or {}).get("summary", "")
        other = [c for c in last.get("chars", []) if c != char_name]
        if summary and other:
            return f"최근에 {other[0]}와(과) 이런 일이 있었다: {summary}."
    return ""


def soliloquy_situation(char_name: str, recent_ctx: str = "", seed: Optional[str] = None) -> str:
    """혼잣말 상황을 LLM 없이 만든다(예전엔 판정 모델을 한 번 더 호출했다).
    성격·말투는 시스템 프롬프트에 이미 있으므로, 여기서는 '무엇에 대해'만 던져 준다."""
    seed = seed or random.choice(_SOLILOQUY_SEEDS)
    if PROMPT_STYLE == "compact":
        return f"(혼자 있다. {recent_ctx} '{seed}'에 대한 혼잣말 한마디.)".replace("  ", " ")
    return (f"(지금 {char_name}은(는) 혼자 있다. {recent_ctx} "
            f"'{seed}'에 대해 문득 떠오른 혼잣말을 네 성격대로 한 문장만 해.)").replace("  ", " ")


EXCHANGE_RULES = """너는 AI 캐릭터들이 사는 섬에서 오가는 대화를 쓰는 작가야. 두 캐릭터의 성격과 말투를 그대로 살려 짧은 대화를 써.

[대사 규칙]
- 모든 대사는 자연스러운 한국어 구어체 존댓말(~요, ~죠)로 끝낸다. 반말·중국어·영어 금지.
- 대사 하나는 한 문장, 길어도 두 문장. 설명을 늘어놓지 말고 핵심만 툭 던진다.
- 번역투(~드립니다, ~하겠습니다, ~입니다)와 비서 말투("도와드릴까요", "필요하면 말씀하세요") 금지.
- line 에는 대사 텍스트만 쓴다. 이름·라벨·따옴표를 붙이지 않는다.
- 두 사람의 관계와 감정이 말투에 자연스럽게 묻어나게 한다.
- 플레이어를 부를 때는 '플레이어'라고 부른다."""


def _build_exchange_messages_full(a: str, b: str, turns: int, rel_desc: str, recent_ctx: str, topic_ctx: str,
                                  history_text: str = "", forced_topic: Optional[str] = None,
                                  asker: Optional[str] = None) -> tuple:
    """(full 프롬프트) 상황·대사 N줄(화자 포함 객체)·플레이어에게 건네는 말·요약·관계 판정."""
    ca, cb = DEFAULT_CHARACTERS[a], DEFAULT_CHARACTERS[b]
    system = f"""{EXCHANGE_RULES}

[{a}]
성격: {ca['personality']}
말투: {ca['speech_style']}

[{b}]
성격: {cb['personality']}
말투: {cb['speech_style']}

[{a}와(과) {b}의 관계]
{rel_desc}

[최근 흐름]
{recent_ctx}
{('[방금 오간 말]' + chr(10) + history_text) if history_text else ''}

[플레이어가 꺼낸 화제]
{topic_ctx}"""

    order = " → ".join([a, b] * ((turns + 1) // 2))
    order = " → ".join(order.split(" → ")[:turns])
    if forced_topic:
        opening = (f"방금 플레이어가 '{forced_topic}'(이)라는 화제를 꺼냈어. "
                   f"situation 에는 두 사람이 이 화제를 두고 이야기하게 된 상황을 한 문장으로 쓰고, "
                   f"{a}이(가) {b}에게 이 화제에 대한 자기 생각이나 궁금한 점을 먼저 건네며 시작해.")
    else:
        opening = ("situation 에는 지금 섬에서 두 사람 사이에 자연스럽게 일어날 법한 상황을 한 문장으로 써. "
                   "친밀도가 낮으면 어색하거나 사소한 충돌, 높으면 편안하거나 장난스러운 상황이 어울려. "
                   "그 상황에서 대화를 시작해.")
    task = [
        opening,
        f"lines 에는 대사 정확히 {turns}줄을 이 순서로 써: {order}.",
    ]
    if asker:
        task.append(f"player_question 에는 대화가 끝난 뒤 {asker}이(가) 옆에서 듣던 플레이어를 쳐다보며 "
                    "대화에 끌어들이듯 가볍게 건네는 말을 한 문장으로 써.")
    task.append("summary 에는 이 대화에서 있었던 일을 10자 이내로 요약해.")
    task.append("delta 에는 이 대화로 두 사람 사이가 어떻게 변했는지 -3~3 정수로 "
                "(따뜻·친근하면 +, 평범하면 0, 차갑거나 다투면 -), "
                "emotion 에는 friendly/neutral/curious/tense/hostile 중 하나를 써.")

    props = {
        "situation": {"type": "string"},
        "lines": {
            "type": "array",
            "minItems": turns, "maxItems": turns,
            "items": {
                "type": "object",
                "properties": {"speaker": {"type": "string", "enum": [a, b]},
                               "line": {"type": "string"}},
                "required": ["speaker", "line"],
            },
        },
        "summary": {"type": "string"},
        "delta": {"type": "integer"},
        "emotion": {"type": "string", "enum": sorted(_EMOTIONS)},
    }
    required = ["situation", "lines", "summary", "delta", "emotion"]
    if asker:
        props["player_question"] = {"type": "string"}
        required.insert(2, "player_question")
    schema = {"type": "object", "properties": props, "required": required}

    messages = [{"role": "system", "content": system},
                {"role": "user", "content": "\n".join(task)}]
    return messages, schema


EXCHANGE_RULES_COMPACT = """너는 섬 캐릭터들의 대화를 쓰는 작가다.
대사마다 그 캐릭터의 말버릇 그대로, 한국어 존댓말(~요/~죠) 한 문장. 이름표·따옴표·설명 금지. 유저는 '플레이어'라고 부른다."""

# 두 캐릭터가 나눌 화제 씨앗(LLM으로 상황을 지어내는 대신 코드에서 고른다 → 출력 토큰 절약)
_PAIR_SEEDS = ["어제 카드게임", "섬의 날씨", "플레이어", "서로의 버릇", "요즘 떠도는 소문",
               "다음 내기", "심심한 오후", "오늘 기분", "누가 더 운이 좋은지"]


def _turn_order(a: str, b: str, turns: int) -> str:
    return " → ".join(([a, b] * turns)[:turns])


def build_exchange_messages(a: str, b: str, turns: int, rel_desc: str, recent_ctx: str, topic_ctx: str,
                            history_text: str = "", forced_topic: Optional[str] = None,
                            asker: Optional[str] = None, with_story: bool = False,
                            seed: Optional[str] = None) -> tuple:
    """NPC 두 명의 대화 묶음을 LLM 1회로 만들기 위한 (messages, JSON 스키마).
    압축 모드: 출력은 대사 문자열 배열 + (선택)플레이어에게 건네는 말 + 관계 판정뿐.
               상황·요약은 with_story=True(자율 이벤트 기록용)일 때만 받는다.
    (bench_models.py 도 이 함수를 그대로 쓴다)"""
    if PROMPT_STYLE != "compact":
        return _build_exchange_messages_full(a, b, turns, rel_desc, recent_ctx, topic_ctx,
                                             history_text, forced_topic, asker)
    topic = forced_topic or seed or random.choice(_PAIR_SEEDS)
    system = f"{EXCHANGE_RULES_COMPACT}\n\n{character_card(a)}\n\n{character_card(b)}\n\n둘의 관계: {rel_desc}"
    if recent_ctx and not recent_ctx.startswith("아직"):
        system += f"\n{recent_ctx}"
    if history_text:
        system += f"\n방금 오간 말:\n{history_text}"

    task = [f"화제: {topic}" + (" (방금 플레이어가 꺼낸 화제)" if forced_topic else "")]
    if with_story:
        task.append("situation: 지금 상황 한 문장.")
    task.append(f"lines: {_turn_order(a, b, turns)} 순서로 대사 {turns}줄.")
    if asker:
        task.append(f"ask: 끝으로 {asker}이(가) 옆에서 듣던 플레이어에게 건네는 한마디.")
    if with_story:
        task.append("summary: 있었던 일 10자 이내.")
    task.append("delta: 대화 뒤 둘 사이 변화(-3~3). mood: 분위기.")
    task.append("모든 대사는 존댓말(~요/~죠) 한 문장. 예시 대사를 그대로 베끼지 않는다.")

    props = {}
    if with_story:
        props["situation"] = {"type": "string"}
    props["lines"] = {"type": "array", "minItems": turns, "maxItems": turns, "items": {"type": "string"}}
    if asker:
        props["ask"] = {"type": "string"}
    if with_story:
        props["summary"] = {"type": "string"}
    props["delta"] = {"type": "integer"}
    props["mood"] = {"type": "string", "enum": sorted(_EMOTIONS)}
    schema = {"type": "object", "properties": props, "required": list(props)}
    return [{"role": "system", "content": system}, {"role": "user", "content": "\n".join(task)}], schema


_JSON_STR = _re.compile(r'"((?:[^"\\]|\\.)*)"')


def parse_exchange(raw: str, a: str, b: str, turns: int) -> Optional[dict]:
    """대화 묶음 응답(JSON)을 검증·정리. 압축/기존 형식 모두 받는다. 실패하면 None."""
    data = None
    try:
        data = json.loads(raw)
    except Exception:
        m = _re.search(r"\{.*\}", raw or "", _re.S)
        if m:
            try:
                data = json.loads(m.group(0))
            except Exception:
                data = None
    if data is None:
        # 출력이 중간에 잘린 경우: lines 배열 안의 완성된 문자열만이라도 건진다.
        m = _re.search(r'"lines"\s*:\s*\[(.*)', raw or "", _re.S)
        if not m:
            return None
        data = {"lines": [json.loads(f'"{s}"') for s in _JSON_STR.findall(m.group(1).split("]")[0])]}
    if not isinstance(data, dict):
        return None

    speakers = [a, b]
    lines = []
    for it in (data.get("lines") or [])[:turns]:
        text = clean_line(it.get("line", "") if isinstance(it, dict) else str(it))
        if text:
            # 화자는 순서로 강제한다(모델이 헷갈려도 번갈아 말하도록).
            lines.append({"speaker": speakers[len(lines) % 2], "line": text})
    if not lines:
        return None
    try:
        delta = max(-3, min(3, int(data.get("delta", 0))))
    except Exception:
        delta = 0
    emotion = str(data.get("mood", data.get("emotion", "neutral"))).lower()
    if emotion not in _EMOTIONS:
        emotion = "neutral"
    return {
        "situation": _strip_quotes(str(data.get("situation", "")).strip()),
        "lines": lines,
        "player_question": clean_line(str(data.get("ask", data.get("player_question", "")))),
        "summary": str(data.get("summary", "")).strip()[:20] or None,
        "delta": delta,
        "emotion": emotion,
    }


def _player_topics(n: int = 3) -> list:
    with _rel_lock:
        data = _load_data()
    return [t.get("text", "") for t in data.get("topics", [])[-n:] if t.get("text")]


def generate_exchange_single(a: str, b: str, turns: int, forced_topic: Optional[str] = None,
                             asker: Optional[str] = None, with_story: bool = False) -> Optional[dict]:
    """두 캐릭터 대화 묶음을 LLM 1회로 생성. 실패하면 None(호출한 쪽이 예전 방식으로 대체)."""
    rel_desc, recent_ctx, topic_ctx = _world_context(a, b)
    compact = PROMPT_STYLE == "compact"
    with _conv_lock:
        recent = [h for h in conversation_history[-(3 if compact else 6):] if h.get("speaker") != "상황"]
    history_text = "\n".join(f"{h['speaker']}: {h['message'][:60] if compact else h['message']}" for h in recent)

    seed = None
    if compact and not forced_topic:
        topics = _player_topics()
        # 플레이어가 꺼냈던 화제를 가끔 다시 꺼내 '주제가 퍼지는' 느낌을 유지
        seed = random.choice(topics) if topics and random.random() < 0.4 else random.choice(_PAIR_SEEDS)
    messages, schema = build_exchange_messages(a, b, turns, rel_desc, recent_ctx, topic_ctx, history_text,
                                               forced_topic, asker, with_story, seed)
    if compact:
        num_predict = 50 + 45 * turns + (45 if asker else 0) + (60 if with_story else 0)
    else:
        num_predict = 160 + 90 * turns
    try:
        raw = ollama_chat(MODEL_NAME, messages, temperature=0.85, num_predict=num_predict,
                          fmt=schema, kind="exchange")
    except Preempted:
        raise
    except Exception as e:
        print(f"[exchange single] 실패 → 예전 방식으로 대체: {e}")
        return None
    result = parse_exchange(raw, a, b, turns)
    if result is None:
        print(f"[exchange single] JSON 해석 실패 → 예전 방식으로 대체: {raw[:120]!r}")
        return None
    if not result["situation"]:
        result["situation"] = f"{a}와(과) {b}의 대화 — {forced_topic or seed or '잡담'}"
    return result


def build_reaction_messages(chars: list, player_rank: int, rel_by_char: dict) -> tuple:
    """카드게임 결과에 대한 캐릭터 전원의 한마디를 LLM 1회로 받기 위한 (messages, 스키마)."""
    if PROMPT_STYLE == "compact":
        blocks = [f"{character_card(n)}\n플레이어와: {compact_relation(rel_by_char.get(n, {}))}" for n in chars]
        system = EXCHANGE_RULES_COMPACT + "\n\n" + "\n\n".join(blocks)
        user = f"방금 플레이어가 4인 카드게임에서 {player_rank}등을 했다. 각자 플레이어에게 할 한마디."
    else:
        blocks = []
        for name in chars:
            info = DEFAULT_CHARACTERS[name]
            rel = rel_by_char.get(name, {})
            blocks.append(f"[{name}]\n성격: {info['personality']}\n말투: {info['speech_style']}\n"
                          f"플레이어와의 관계: 친밀도 {rel.get('intimacy', 0):+d}, 신뢰 {rel.get('trust', 0)}/100")
        system = EXCHANGE_RULES + "\n\n" + "\n\n".join(blocks)
        user = (f"방금 플레이어가 4명이 하는 카드게임에서 {player_rank}등을 했어. "
                f"각 캐릭터가 이 결과를 보고 플레이어에게 할 법한 짧은 한마디를 자기 성격과 말투 그대로 한 문장씩 써.")
    schema = {"type": "object",
              "properties": {name: {"type": "string"} for name in chars},
              "required": list(chars)}
    return [{"role": "system", "content": system}, {"role": "user", "content": user}], schema


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
    if FAST_MODE:
        speakers = [char_a, char_b]
        random.shuffle(speakers)
        res = generate_exchange_single(speakers[0], speakers[1], turns, with_story=True)
        if res is not None:
            now = time.time()
            event_history = [{"speaker": "상황", "message": res["situation"], "timestamp": now}]
            event_history += [{"speaker": l["speaker"], "message": l["line"], "timestamp": now}
                              for l in res["lines"]]
            with _conv_lock:
                conversation_history.extend(event_history[1:])
                del conversation_history[:-100]
            # 예전 판정(친밀도 -10~10, 신뢰 -5~5)과 비슷한 폭이 되도록 환산
            delta_i, delta_t = res["delta"] * 3, (1 if res["delta"] > 0 else -1 if res["delta"] < 0 else 0)
            update_relationship(char_a, char_b, delta_i, delta_t, res["emotion"], res["summary"])
            _log_event(res["situation"], [char_a, char_b], event_history,
                       {"delta_intimacy": delta_i, "delta_trust": delta_t,
                        "emotion": res["emotion"], "summary": res["summary"]})
            return event_history

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
    _log_event(situation, [char_a, char_b], event_history,
               {"delta_intimacy": delta_i, "delta_trust": delta_t, "emotion": emotion, "summary": summary})
    return event_history


def _log_event(situation: str, chars: list, dialogue: list, result: dict):
    with _rel_lock:
        data = _load_data()
        data.setdefault("events", []).append({
            "situation": situation,
            "chars": chars,
            "dialogue": dialogue,
            "result": result,
            "timestamp": time.time()
        })
        if len(data["events"]) > 50:
            data["events"] = data["events"][-50:]
        _save_data(data)

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

# 혼잣말 미리 만들어 두기
_pool_lock = threading.Lock()
_soliloquy_pool: dict = {}     # 캐릭터 → [(대사, 만든 시각)]
_line_requested: dict = {}     # 캐릭터 → 마지막 /character/line 요청 시각
POOL_TTL = 180                 # 이보다 오래된 혼잣말은 버림(맥락이 낡음)
PREGEN_WINDOW = 60             # 최근 이 시간 안에 요청이 있었던 캐릭터만 미리 만듦

# ========================
# 백그라운드 작업자 — 관계 판정·혼잣말 미리 만들기를 플레이어 요청 사이 빈틈에만 실행
# ========================
_bg_queue: "queue.Queue" = queue.Queue()
_bg_started = False


def submit_background(fn, *args):
    """응답을 돌려준 뒤에 해도 되는 LLM 작업(예: 관계 판정)을 저우선 큐에 넣는다."""
    _bg_queue.put((fn, args, 0))


def _make_soliloquy(char_name: str) -> str:
    """혼잣말 한 줄. 빠른 모드면 LLM 1회, 아니면 예전처럼 상황 생성 + 대사(2회)."""
    if FAST_MODE:
        situation = soliloquy_situation(char_name, _soliloquy_recent_ctx(char_name))
    else:
        situation = generate_soliloquy_prompt(char_name)
    with _conv_lock:
        recent = list(conversation_history[-6:])
    return call_ollama(char_name, recent, user_message=situation)


def _pregen_one():
    """혼잣말 풀이 빈 캐릭터 하나를 골라 한 줄 미리 만든다(한 번에 하나)."""
    if not PREGEN_SOLILOQUY or _prio.busy(quiet=3.0):
        return
    now = time.time()
    with _pool_lock:
        for name in list(_soliloquy_pool):
            _soliloquy_pool[name] = [p for p in _soliloquy_pool[name] if now - p[1] < POOL_TTL]
        targets = [n for n, t in _line_requested.items()
                   if now - t < PREGEN_WINDOW and not _soliloquy_pool.get(n)]
    if not targets:
        return
    name = min(targets, key=lambda n: _line_requested[n])   # 가장 오래 기다린 캐릭터부터
    try:
        line = _make_soliloquy(name)
    except Preempted:
        return
    if line and not line.startswith("("):
        with _pool_lock:
            _soliloquy_pool.setdefault(name, []).append((line, time.time()))


def _bg_worker():
    _tls.background = True        # 이 스레드의 LLM 호출은 플레이어 요청이 오면 양보한다
    while True:
        try:
            fn, args, tries = _bg_queue.get(timeout=1.0)
        except queue.Empty:
            try:
                _pregen_one()
            except Exception as e:
                print(f"[pregen] 실패: {e}")
            continue
        while _prio.busy():
            time.sleep(0.3)
        try:
            fn(*args)
        except Preempted:
            if tries < 5:
                _bg_queue.put((fn, args, tries + 1))   # 나중에 다시
        except Exception as e:
            print(f"[background] {getattr(fn, '__name__', fn)} 실패: {e}")


def _start_background_worker():
    global _bg_started
    if not _bg_started:
        _bg_started = True
        threading.Thread(target=_bg_worker, daemon=True, name="sevens-bg").start()

# ========================
# 자율 루프
# ========================
def autonomous_loop(interval_seconds: int):
    global current_speaker_index, is_loop_running
    is_loop_running = True
    _tls.background = True        # 자율 루프도 플레이어 요청이 오면 양보

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
            except Preempted:
                continue
            except Exception as e:
                print(f"[loop error] {e}")
        else:
            speaker = random.choice(chars)
            try:
                line = _make_soliloquy(speaker)
            except Preempted:
                continue
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
def user_message(req: UserMessageRequest):
    with interactive():
        return _user_message(req)


def _user_message(req: UserMessageRequest):
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
    # 대화 내용에 따라 플레이어-캐릭터 호감도를 소폭 조정(응답 뒤, 플레이어 요청 사이 빈틈에).
    submit_background(
        judge_and_adjust_relationship, "플레이어", responder,
        f"플레이어: {req.message}\n{responder}: {reply}")
    return {"speaker": responder, "reply": reply}

class GroupMessageRequest(BaseModel):
    message: str
    characters: list[str]   # 응답할 캐릭터들(말하는 순서대로, 최대 3)

@app.post("/conversation/group")
def group_message(req: GroupMessageRequest):
    """플레이어 한 마디에 여러 캐릭터가 '차례로' 반응한다(진짜 3자 대화).
    앞 캐릭터의 대답까지 맥락에 넣어 다음 캐릭터가 이어 말하므로, 서로 주고받는 느낌이 난다."""
    with interactive():
        return _group_message(req)


def _group_message(req: GroupMessageRequest):
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
        submit_background(
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
    with interactive():
        return _conversation_next()


def _conversation_next():
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
    now = time.time()
    with _pool_lock:
        _line_requested[char_name] = now
        pool = [p for p in _soliloquy_pool.get(char_name, []) if now - p[1] < POOL_TTL]
        line = pool.pop(0)[0] if pool else None
        _soliloquy_pool[char_name] = pool
    if line is None:
        line = _make_soliloquy(char_name)
    return {"speaker": char_name, "line": line}

class ExchangeRequest(BaseModel):
    char_a: str
    char_b: str
    turns: int = 3

@app.post("/conversation/exchange")
def conversation_exchange(req: ExchangeRequest):
    with interactive():
        return _conversation_exchange(req)


def _conversation_exchange(req: ExchangeRequest):
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

    # 플레이어가 방금 꺼낸 화제가 있으면, 이번 대화 첫 발화를 '무조건' 그 주제로 시작시킨다(1회 소비).
    forced_topic = _pending_topic
    _pending_topic = None

    # ── 빠른 모드: 상황·대사·플레이어에게 건네는 말·관계 판정을 LLM 1회로 ──
    if FAST_MODE:
        # 가끔(35%) 마지막 화자가 옆에서 듣던 플레이어를 향해 말을 건넨다.
        asker = ([a, b][(turns - 1) % 2]) if random.random() < 0.35 else None
        res = generate_exchange_single(a, b, turns, forced_topic, asker)
        if res is not None:
            now = time.time()
            with _conv_lock:
                for l in res["lines"]:
                    conversation_history.append({"speaker": l["speaker"], "message": l["line"], "timestamp": now})
                if asker and res["player_question"]:
                    conversation_history.append({"speaker": asker, "message": res["player_question"], "timestamp": now})
                del conversation_history[:-100]
            update_relationship(a, b, res["delta"], 1 if res["delta"] > 0 else 0, res["emotion"])
            return {"lines": res["lines"], "situation": res["situation"],
                    "player_question": res["player_question"] if asker else "", "mode": "single"}

    # ── 예전 방식(빠른 모드 꺼짐 또는 JSON 생성 실패 시) ──
    # 대화 주제 씨앗(관계·성격 기반). 첫 발화에만 상황으로 주입.
    situation = generate_event_situation(a, b)

    speakers = [a, b]
    with _conv_lock:
        local_history = list(conversation_history[-8:])

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
    submit_background(judge_and_adjust_relationship, a, b, convo_text)

    return {"lines": lines, "situation": situation, "player_question": player_question, "mode": "multi"}

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

    with interactive():
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

def approach_situation(name: str, recent_ctx: str = "", event_ctx: str = "") -> str:
    """플레이어가 다가왔을 때의 상황 설명. (bench_models.py 도 사용)"""
    recent_line = f"최근 {name}이(가) 한 말: {recent_ctx}" if recent_ctx else ""
    if PROMPT_STYLE == "compact":
        extra = " ".join(x for x in (recent_line[:60], event_ctx[:60]) if x)
        return f"(혼자 있다가 다가오는 플레이어를 발견했다. {extra} 첫마디를 해.)".replace("  ", " ")
    return f"""플레이어가 갑자기 {name}에게 다가왔다.
{name}은(는) 지금 혼자 있다가 플레이어를 발견한 상황이다.
{recent_line}
{event_ctx}

이 상황에서 {name}이(가) 플레이어를 알아보고 자연스럽게 반응하는 첫 한마디를 해."""


@app.post("/player/approach")
def player_approach(name: str):
    with interactive():
        return _player_approach(name)


def _player_approach(name: str):
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

    situation = approach_situation(name, recent_ctx, event_ctx)

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
    with interactive():
        return _report_game_result(req)


def _report_game_result(req: GameResultRequest):
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
    rel_by_char = {}
    for char_name in chars:
        update_relationship("플레이어", char_name, base_intimacy, base_trust, "neutral", result_desc)
        rel_by_char[char_name] = get_relationship("플레이어", char_name)

    # 빠른 모드: 캐릭터 전원의 반응을 LLM 1회로
    reactions = {}
    if FAST_MODE:
        try:
            msgs, schema = build_reaction_messages(chars, req.player_rank, rel_by_char)
            raw = ollama_chat(MODEL_NAME, msgs, temperature=0.85,
                              num_predict=(45 if PROMPT_STYLE == "compact" else 80) * len(chars),
                              fmt=schema, kind="reactions")
            parsed = json.loads(raw)
            reactions = {c: clean_line(str(parsed.get(c, ""))) for c in chars}
            reactions = {c: r for c, r in reactions.items() if r}
        except Exception as e:
            print(f"[game result] 한 번에 생성 실패 → 캐릭터별 생성: {e}")
            reactions = {}

    for char_name in chars:
        if char_name in reactions:
            continue
        rel = rel_by_char[char_name]

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


# ========================
# LLM 상태 확인 (디버그·발표용)
# ========================
@app.get("/llm/status")
def llm_status():
    """현재 모델 설정과 호출 통계. 브라우저로 http://localhost:8000/llm/status 를 열어 확인."""
    with _stats_lock:
        stats = {k: {"calls": v["calls"],
                     "avg_sec_recent": round(sum(v["recent"]) / len(v["recent"]), 2) if v["recent"] else None,
                     "recent": v["recent"][-10:]}
                 for k, v in _stats.items()}
    with _pool_lock:
        pool = {k: len(v) for k, v in _soliloquy_pool.items()}
    loaded = []
    try:
        loaded = [{"name": m.get("name"),
                   "size_gb": round(m.get("size", 0) / 1e9, 2),
                   "gpu_percent": round(100 * m.get("size_vram", 0) / m["size"]) if m.get("size") else 0}
                  for m in requests.get(f"{OLLAMA_URL}/api/ps", timeout=3).json().get("models", [])]
    except Exception:
        pass
    return {
        "model": MODEL_NAME,
        "judge_model": JUDGE_MODEL,
        "fast_mode": FAST_MODE,
        "pregen_soliloquy": PREGEN_SOLILOQUY,
        "num_ctx": NUM_CTX,
        "capabilities": {m: sorted(c) for m, c in _caps_cache.items()},
        "ollama_loaded": loaded,
        "player_requests_in_flight": _prio.active,
        "background_queue": _bg_queue.qsize(),
        "soliloquy_pool": pool,
        "calls": stats,
    }
