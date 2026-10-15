# 카드게임 하이라이트 자동 검출 · 캐릭터 반응

제안서 핵심 기능 "하이라이트 자동 검출 및 연출 시스템"과 "캐릭터 상황 반응"의 구현 정리.

## 구조

```
CardGameManager ──TurnEnded(좌석, 이번 턴에 낸 카드)──▶ SevensHighlightDetector ──HighlightEvent──▶ HighlightDirector
      ▲                                                     (순수 C#, Unity 의존 없음)              │ 배너·번쩍임·말풍선
      └──────────────────────── HoldAI(초) : 큰 순간엔 AI 턴을 잠깐 멈춤 ◀──────────────────────────┘
                                                                                                   │ 대사
                                         서버 /game/reaction_bank (LLM이 미리 만든 캐릭터 대사) ──▶ CharacterReactionLines ◀── 기본 대사
게임 종료 ─▶ GameResultReporter ─▶ 서버 /game/result (순위 + 명장면) ─▶ 캐릭터 결과 반응, 관계 반영, 다음 판 대사 새로 생성
```

| 파일 | 역할 |
|---|---|
| `Scripts/CardGame/Highlight/SevensHighlightDetector.cs` | 턴이 확정될 때마다 하이라이트 판단 (사람의 '되돌리기'가 반영된 최종 결과 기준) |
| `Scripts/CardGame/Highlight/HighlightDirector.cs` | 연출(배너·번쩍임·말풍선·프로필 흔들림), 캐릭터 선택, 서버 대사 받기. `CardGameAddons`가 CardGame 씬에 자동으로 붙임 |
| `Scripts/CardGame/Highlight/CharacterReactionLines.cs` | 캐릭터 × 상황 × 관점(본인/플레이어/다른 캐릭터) 대사. 서버 대사 70%, 기본 대사 30% |
| `Scripts/CardGame/Core/GameManager.cs` | `GameStarted`, `TurnEnded` 이벤트, `GetHandCounts()`, `HoldAI()`, AI 이름을 세비-호/류/진으로 |
| `Scripts/GameResultReporter.cs` | 실제 순위(남은 손패 순), 패스 수, 명장면을 서버로 보내고 결과 화면에 표시 |

## 검출 규칙

| 하이라이트 | 조건 | 강도 | 연출 |
|---|---|---|---|
| 마지막 한 장 | 손패 2장 이상 → 1장 | 3 | 큰 배너 + 번쩍임 + AI 잠깐 멈춤 + 본인·라이벌 대사 |
| 역전 | 단독 선두(최소 손패)가 바뀌고, 새 선두가 그동안 3장 이상 뒤처졌던 적이 있음 (4턴 쿨다운) | 3 | 〃 |
| 3장 몰아치기 | 한 턴에 3장을 다 내서 4장 이하가 됨 | 2 | 배너 + 대사 |
| 접전 | 두 명 이상이 2장 이하 (판당 1번) | 2 | 배너 + 대사 |
| 막힘 | 같은 사람이 2연속 패스(4연속이면 강도 2) | 1~2 | 배너 (+ 대사) |
| 곧 끝남 / 줄 완성 | 처음 3장 이하 / 한 문양 A~K 완성 | 1 | 작은 배너 |

## 시뮬레이션 검증 (3000판, 사람 30%·AI 20% 확률로 추가 장을 놓치는 모델)

| 항목 | 판당 횟수 |
|---|---|
| 마지막 한 장 | 1.53 |
| 역전 | 0.89 |
| 3장 몰아치기 | 2.62 (조건을 넣기 전에는 7.32 → 너무 잦아서 막판 스퍼트만 남김) |
| 접전 | 0.84 |
| 큰 순간(강도 3) | 2.42 |
| 캐릭터 반응이 1번 이상 나온 판 | 100% |

평균 22턴짜리 판에서 캐릭터 반응은 약 6번, 크게 연출하는 순간은 약 2.4번이다.

## 직접 돌려 보기 (mono)

```bash
# 감지기 시뮬레이션 + 규칙 테스트
csc -langversion:9.0 -r:System.Core.dll -out:sim.exe \
  Assets/sevens/Scripts/CardGame/Core/{Card,Suit,FieldLine}.cs \
  Assets/sevens/Scripts/CardGame/Highlight/SevensHighlightDetector.cs docs/highlight_sim/Sim.cs
mono sim.exe 3000

# 대사 테스트 (모든 상황·관점·캐릭터에 존댓말 대사가 있는지)
csc -langversion:9.0 -r:System.Core.dll -out:lines.exe \
  Assets/sevens/Scripts/CardGame/Core/{Card,Suit}.cs \
  Assets/sevens/Scripts/CardGame/Highlight/{SevensHighlightDetector,CharacterReactionLines}.cs docs/highlight_sim/LinesTest.cs
mono lines.exe
```
