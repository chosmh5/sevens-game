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
