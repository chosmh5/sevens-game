from fastapi import FastAPI
from pydantic import BaseModel
from typing import Optional
import requests
import time
import threading

app = FastAPI()

OLLAMA_CHAT_URL = "http://localhost:11434/api/chat"
MODEL_NAME = "exaone3.5:7.8b"

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
}

# ========================
# 대화 상태
# ========================
conversation_history = []
active_characters = ["세비-호"]
is_loop_running = False
current_speaker_index = 0

# ========================
# Ollama chat API 호출
# ========================
def call_ollama(speaker_name: str, history: list, user_message: Optional[str] = None) -> str:
    char = DEFAULT_CHARACTERS.get(speaker_name)
    if not char:
        return "(알 수 없는 캐릭터)"

    others = [c for c in active_characters if c != speaker_name]

    if others:
        talk_target = ", ".join(others)
    else:
        talk_target = "플레이어(유저)"

    system_prompt = f"""[핵심 규칙] 반드시 한국어 존댓말(~요)로만 대답해. 반말 절대 금지.

너는 '{speaker_name}'라는 이름의 AI 캐릭터야.
대화 상대: {talk_target}

[성격]
{char['personality']}

[말투]
{char['speech_style']}

[반드시 지켜야 할 규칙]
- 반드시 자연스러운 한국어 구어체로만 말해. 중국어 절대 금지.
- 1문장만 말해. 길게 설명하지 마.
- 이름이나 라벨을 앞에 붙이지 마. 대사 텍스트만 출력해.
- 유저는 항상 '플레이어'라고 불러.
- 번역투 표현(~드립니다, ~하겠습니다, ~입니다) 사용 금지.
- 질문이나 대화 내용과 관련 없는 말은 하지 마. 주제에서 벗어나지 마.
- 문장 끝에 질문이나 제안을 붙이지 마. 상대방이 말하면 그냥 반응만 해.
- 존댓말을 써. 예시처럼 자연스럽게 '~요'로 끝나는 문장을 써."""

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
            "options": {"temperature": 0.8, "num_predict": 30},
            "messages": messages
        }, timeout=30)
        reply = res.json().get("message", {}).get("content", "").strip()

        # 이름 접두사 제거
        for char_name in list(DEFAULT_CHARACTERS.keys()) + ["유저", "플레이어"]:
            for prefix in [f"{char_name}: ", f"[{char_name}] ", f"{char_name}:"]:
                if reply.startswith(prefix):
                    reply = reply[len(prefix):].strip()

        # 첫 문장만 사용
        for sep in ["。", ". ", "! ", "? ", ".\n", "!\n", "?\n"]:
            if sep in reply:
                reply = reply.split(sep)[0] + sep.strip()
                break

        return reply.strip()
    except Exception as e:
        return f"(응답 오류: {e})"

# ========================
# 대화 루프 (캐릭터 2명 이상일 때 자동 진행)
# ========================
def conversation_loop(interval_seconds: int):
    global current_speaker_index, is_loop_running
    is_loop_running = True

    while is_loop_running:
        if len(active_characters) < 2:
            time.sleep(5)
            continue

        speaker = active_characters[current_speaker_index % len(active_characters)]
        reply = call_ollama(speaker, conversation_history)

        conversation_history.append({
            "speaker": speaker,
            "message": reply,
            "timestamp": time.time()
        })

        if len(conversation_history) > 100:
            conversation_history.pop(0)

        current_speaker_index += 1
        time.sleep(interval_seconds)

loop_thread = None

# ========================
# 엔드포인트
# ========================

@app.get("/")
def root():
    return {"status": "ok", "loop_running": is_loop_running, "active_characters": active_characters}

@app.post("/loop/start")
def start_loop(interval: int = 30):
    global loop_thread, is_loop_running
    if len(active_characters) < 2:
        return {"error": "자동 루프는 캐릭터 2명 이상 필요"}
    if is_loop_running:
        return {"message": "이미 실행 중"}
    loop_thread = threading.Thread(target=conversation_loop, args=(interval,), daemon=True)
    loop_thread.start()
    return {"message": f"루프 시작 ({interval}초 간격)"}

@app.post("/loop/stop")
def stop_loop():
    global is_loop_running
    is_loop_running = False
    return {"message": "루프 중지"}

@app.get("/conversation")
def get_conversation(limit: int = 5):
    return {
        "messages": conversation_history[-limit:],
        "active_characters": active_characters
    }

# 유저 → 캐릭터 대화
class UserMessageRequest(BaseModel):
    message: str
    target: Optional[str] = None

@app.post("/conversation/user")
def user_message(req: UserMessageRequest):
    conversation_history.append({
        "speaker": "플레이어",
        "message": req.message,
        "timestamp": time.time()
    })

    responder = req.target if req.target in active_characters else active_characters[0]
    reply = call_ollama(responder, conversation_history, req.message)

    conversation_history.append({
        "speaker": responder,
        "message": reply,
        "timestamp": time.time()
    })

    return {"speaker": responder, "reply": reply}

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
    conversation_history.clear()
    return {"message": "초기화 완료"}