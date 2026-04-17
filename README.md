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
