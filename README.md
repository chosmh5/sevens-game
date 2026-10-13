# Sevens Game

AI 캐릭터 대화 서버 + Android 위젯 프로젝트

---

## 서버 실행 방법

### 1. Ollama 실행 (첫 번째 터미널)
```bash
ollama serve
```

### 2. FastAPI 서버 실행 (두 번째 터미널)
```bash
cd server
uvicorn main:app --reload --host 0.0.0.0
```

> 실기기 위젯 연결 시 `--host 0.0.0.0` 필수

---

## 대화 테스트

```bash
curl -X POST http://localhost:8000/conversation/user -H "Content-Type: application/json" -d "{\"message\": \"안녕\"}"
```

---

## Android 위젯

- 위젯 앱: `C:\Users\kkk42\AndroidStudioProjects\SevensWidget`
- 서버 IP 설정: `SevensWidget.kt` 상단 `SERVER_URL` 변수
- 실기기 사용 시 PC와 같은 Wi-Fi 연결 필요

---

## Unity 클라이언트

- 에디터: Unity **6000.3.14f1** (URP 17.3), `unity-client/` 폴더를 연다
- 씬: `Assets/sevens/Scenes` — TitleScreen → Lobby / MyRoom / CardGame

### 별도로 임포트해야 하는 에셋스토어 패키지

Asset Store EULA상 공개 저장소에 올릴 수 없어서 저장소에서 뺐다.
클론한 뒤 Package Manager → My Assets에서 아래 패키지를 임포트한다.

| 폴더 | 패키지 (퍼블리셔) |
|---|---|
| `Assets/Polytope Studio` | Lowpoly Environments / Lowpoly Village (Polytope Studio) |
| `Assets/WorldSpaceTransitions` | Fading — URP (WorldSpaceTransitions) · `BuildingFadeManager`가 사용 |
| `Assets/BrokenVector` | Low Poly Tree Pack (Broken Vector) |
| `Assets/Suggo Creations` | RETROWAVE SKIES Lite (Suggo Creations) |
| `Assets/sevens/AutoMobileColorGrading` | Auto Mobile Color Grading (ALIyerEdon) |

---

## AI 모델 설정·벤치마크

캐릭터 말투는 `server/main.py`의 `CHARACTER_CARDS`(정체·말버릇·주변·예시)에서 고친다.
모델은 `server/main.py` 맨 위의 `MODEL_NAME`(대사), `JUDGE_MODEL`(판정)에서 바꾼다.
환경변수 `SEVENS_MODEL`, `SEVENS_JUDGE_MODEL`로도 바꿀 수 있다. 둘을 같은 모델로 두면 메모리에 하나만 올라간다.

| 설정 | 기본값 | 설명 |
|---|---|---|
| `SEVENS_FAST` | `1` | 빠른 모드. NPC 대화 묶음·혼잣말·게임 반응을 LLM 1회 호출로 생성. `0`이면 예전 방식 |
| `SEVENS_PREGEN` | `1` | 혼잣말을 빈 시간에 미리 만들어 둠. 플레이어가 말을 걸면 즉시 양보 |
| `SEVENS_NUM_CTX` | `4096` | 컨텍스트 길이 |
| `SEVENS_PROMPT` | `compact` | 압축 프롬프트(캐릭터 카드·관계 한 줄·최근 대화 4줄). `full`이면 예전 긴 프롬프트 |

현재 설정과 호출 통계는 서버를 켠 뒤 `http://localhost:8000/llm/status`에서 볼 수 있다.

### 후보 모델 비교

```bash
cd server
python bench_models.py --pull     # 없는 후보는 받아서 측정 (uvicorn 서버는 끄고 실행)
python bench_models.py --quick    # 빠르게 한 번
python bench_models.py --prompt both   # 압축 vs 예전 프롬프트 비교
```

결과는 `server/bench_results/<시각>/report.html`에 저장된다(속도, 존댓말 준수, 외국어 혼입, JSON 성공률, 실제 대사 비교).

### 서버 테스트 (Ollama 없이)

```bash
cd server
pip install -r requirements.txt
python tests/test_server.py fast
python tests/test_server.py slow
```
