# 프로젝트: 세븐 카드 게임 (졸업작품)

## 프로젝트 개요
트럼프 카드 게임 '세븐(Sevens)' 기반 Android 모바일 게임.
위젯 기반 AI 캐릭터 챗봇 시스템 포함.

## 핵심 목표
- 세븐 규칙 기반 PvP 카드 게임
- 캐릭터 상황 반응 시스템 (승리/패배/역전 등)
- Android 위젯으로 앱 실행 없이 캐릭터와 상호작용

## 기술 스택
- **게임 클라이언트**: Unity Engine (Android 빌드)
- **AI 챗봇**: Qwen 2.5 3B + Ollama + FastAPI (로컬 LLM)
- **UI/UX**: Figma
- **모델링**: Blender
- **버전 관리**: GitHub

## AI 선택 이유
로컬 LLM 방향으로 확정 — 비용 없음, 상업적 이용 가능, 보안

## 현재 상태
- Ollama 설정 완료
- FastAPI 서버 구현 완료 (server/main.py)
- Android 위젯 구현 완료 (SevensWidget)

## 개발 우선순위
1. 카드 게임 로직 (세븐 규칙)
2. FastAPI + Ollama 연동 서버
3. 캐릭터 반응 시스템
4. Android 위젯 인터페이스
5. 멀티플레이 (어려우면 AI 대전으로 대체)
