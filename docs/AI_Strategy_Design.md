# 세븐(Sevens) AI 전략 개선 설계 문서

작성일: 2026-06-03
대상 모듈: `unity-client/Assets/sevens/Scripts/CardGame/Core`

---

## 1. 현황 분석

### 1.1 게임 규칙 요약 (코드 기준)
- 4인(사람 1 + AI 3), 각 12장 분배. 4장의 7은 시작 시 자동으로 필드에 깔림(손패에서 제외, 48장 분배).
- 각 문양(♠♥♦♣)마다 한 줄(`FieldLine`). 7이 깔리면 열리고, 위(8→K)·아래(6→A) 방향으로 **한 칸씩만** 확장.
- 어떤 카드가 낼 수 있는 카드인가 = 해당 문양 줄의 `MinPlaced-1` 또는 `MaxPlaced+1`.
- **한 턴에 최대 3장**(`maxPlaysPerTurn = 3`)까지 낼 수 있음.
- **낼 수 있는 카드가 있으면 최소 1장은 반드시 내야 함**(첫 장 패스 불가). 1장 낸 뒤부터는 더 낼지 / 턴을 끝낼지 자발적으로 선택 가능(2·3번째 장은 선택).
- 낼 카드가 전혀 없을 때만 패스. **패스 페널티 없음**(`PassCount`는 기록만).
- 손패를 먼저 비우면 승리.

### 1.2 현재 AI 구조
```
IAiStrategy.ChooseCard(field, hand) -> Card (또는 null = 패스)
  └ RandomAiStrategy : 낼 수 있는 카드 중 무작위 1장
GameManager.RunAITurn() : ChooseCard를 턴당 정확히 1회 호출 → 최대 1장만 냄
```

### 1.3 핵심 한계 (왜 약한가)
1. **무작위 선택** — 상대를 막거나 자기 손패를 효율적으로 비우는 판단이 전혀 없음.
2. **턴당 1장 고정** — 규칙은 3장까지 허용하는데 AI는 1장만 냄. 사람이 매 턴 3장씩 쏟아내면 AI는 구조적으로 항상 뒤처짐. *현재 가장 큰 불공정 지점.*
3. **인터페이스 표현력 부족** — `ChooseCard`가 단일 카드만 반환해서 (a) 한 턴 여러 장, (b) 선택적 추가 장을 안 내고 멈추는 결정을 표현할 수 없음.
4. **난이도 없음** — 단일 전략이라 초보/숙련 사용자 모두에게 부적절.
5. **상대 상태 무시** — 상대 손패 수, 어떤 카드가 막혀 있는지 등 정보를 활용하지 않음.

---

## 2. 개선 목표

| 목표 | 설명 |
|------|------|
| 공정성 | AI도 턴당 최대 3장까지 사용 (사람과 동일 규칙) |
| 난이도 | Easy / Normal / Hard 3단계 제공, 로비에서 선택 |
| 정직함 | "체감되는" 난이도 차이를 시뮬레이션으로 검증한 뒤 채택 |
| 확장성 | `IAiStrategy`만 갈아끼우면 동작 — GameManager 의존 최소화 |
| 검증성 | 결정적 시드(seed) 기반 시뮬레이션으로 회귀 검증 가능 |

---

## 3. 전략 깊이 분석 (시뮬레이션으로 검증)

처음에는 "끝수 우선 처분 + 상대 이득 최소화" 같은 점수 기반 전략을 설계했으나, **AI vs AI 시뮬레이션으로 검증한 결과 이 게임 변형에서는 카드 선택이 승률에 사실상 영향을 주지 않음**을 확인했다(§9 참조). 근거:

- **규칙 전제:** 낼 수 있는 카드가 있으면 첫 1장은 의무 제출(보류·막기 불가). 줄은 7로 미리 열려 있고, 한 턴 최대 3장.
- 이 조건에서 **한 턴에 낼 수 있는 카드의 '집합'은 순서·선택과 무관하게 고정**된다(인접 연쇄가 결정적). 즉 어떤 카드를 고르든 그 턴에 비우는 장 수가 같다.
- 시뮬레이션 수치: 처리량을 **최대화하든(DFS-MAX) 최소화하든(DFS-MIN) 무작위든 승률이 모두 25.9%로 동일**. 반면 *일부러 1장만 내고 멈추면 6.2%로 폭락*.

### 결론: 유일하게 의미 있는 레버 = "낼 수 있는 만큼 다 내느냐"
- 완벽하게 둬도 승률 상한은 공정 몫(4인 → **~25%**). "Hard가 사람보다 똑똑해서 더 이기는" 것은 이 규칙상 불가능하다.
- 따라서 **난이도는 '더 똑똑한 선택'이 아니라 '실수(추가 장 놓침) 빈도'로 표현**한다.
  - Easy: 추가 장을 자주 놓쳐(조기 종료) 약하게 → 사람이 이기기 쉬움.
  - Hard: 절대 안 놓침(완벽) → 사람 승률이 공정 몫까지 떨어짐.
- 첫 장은 규칙상 의무라 항상 제출(놓침은 2·3번째 선택 장에만 적용).

---

## 4. 제안 아키텍처 — 인터페이스 변경 최소화

핵심 아이디어: **`ChooseCard` 시그니처는 그대로 두고, GameManager가 턴 안에서 루프로 반복 호출**한다.

```
RunAITurn(p):
    plays = 0
    while plays < maxPlaysPerTurn:
        chosen = _ai.ChooseCard(field, p.Hand)
        // 의무 제출 규칙(사람과 동일): 첫 장에서 낼 카드가 있는데 전략이 null을 주면
        // 강제로 가용 카드 1장을 선택(전략 버그 방어 + 규칙 보장).
        if chosen == null && plays == 0 && field.HasAnyPlayable(p.Hand):
            chosen = field.FilterPlayable(p.Hand)[0];
        if chosen == null: break                 // plays>=1에서 null = 자발적 턴 종료
        if !field.TryPlace(chosen): break
        p.Hand.Remove(chosen); plays++
        승리 체크
    if plays == 0: 패스 처리                      // 낼 카드가 정말 없었던 경우만
    NextTurn()
```

이 방식의 장점:
- `null` 반환의 의미가 자연스럽게 확장됨: **첫 호출에 null = (낼 카드 없음) 패스, 1장 이상 낸 뒤 null = 자발적 턴 종료**. 사람 규칙과 정확히 대칭(첫 장 의무 제출은 위 가드로 보장).
- 난이도(실수 빈도)는 추가 장에서 `null`을 반환하는 확률로 표현(§5). 첫 장은 멈출 수 없음.
- 시그니처에 `AiContext ctx`만 추가(이번 턴 제출 수 `PlaysThisTurn` 등 전달). 그 외 코어는 변경 없음.

### 4.1 난이도 선택 주입
카드 선택이 승률 무관(§3)이므로 전략 클래스는 `RandomAiStrategy` 하나로 통일하고, **난이도 = 놓침 확률(overlookChance)** 파라미터로 구현한다.
```csharp
public enum AiDifficulty { Easy, Normal, Hard }

IAiStrategy CreateStrategy(AiDifficulty d, int seed) => d switch {
    AiDifficulty.Easy   => new RandomAiStrategy(seed, overlookChance: 0.55f),
    AiDifficulty.Normal => new RandomAiStrategy(seed, overlookChance: 0.20f),
    AiDifficulty.Hard   => new RandomAiStrategy(seed, overlookChance: 0.00f),
    _                   => new RandomAiStrategy(seed, overlookChance: 0.20f),
};
```
(향후 AI 플레이어마다 다른 난이도를 주고 싶으면 `_ai` 단일 필드를 `IAiStrategy[3]`로 확장)

---

## 5. 난이도별 전략

모든 난이도가 단일 `RandomAiStrategy`를 쓰되 **`overlookChance`(추가 장 놓침 확률)** 만 다르다. 카드 선택은 무작위(승률 무관, §3). 첫 장은 항상 제출.

| 난이도 | overlookChance | 행동 | 사람 체감 |
|--------|----------------|------|-----------|
| **Easy**   | 0.55 | 추가 장을 절반 이상 놓쳐 자주 턴을 일찍 끝냄 | 약함, 이기기 쉬움 |
| **Normal** | 0.20 | 가끔 추가 장을 놓침 | 적당함 |
| **Hard**   | 0.00 | 낼 수 있는 만큼 항상 다 냄(완벽) | 공정 몫(~25%)까지 빡빡함 |

```csharp
// 추가 장(plays>=1)에서만 확률적으로 놓쳐서 조기 종료. 첫 장은 의무라 항상 제출.
if (ctx.PlaysThisTurn >= 1 && rng.NextDouble() < overlookChance) return null;
return playable[rng.Next(playable.Count)];
```

> 왜 점수 기반 휴리스틱을 버렸나: 초기 설계의 `OpponentGain`/끝수 우선 점수는 §9 시뮬레이션에서 무작위 대비 승률 차이가 측정 오차(±0.7%p) 안이었다. 이 규칙에서는 "영리한 선택"이 작동하지 않으므로, 검증된 단일 레버(놓침 빈도)만 남겼다.

---

## 6. GameManager 변경점

| 위치 | 현재 | 변경 |
|------|------|------|
| 필드 | `IAiStrategy _ai` | `AiDifficulty aiDifficulty` + `useLobbyDifficulty` 추가, `CreateStrategy()`로 주입 |
| `RunAITurn` | `ChooseCard` 1회 호출, 1장만 냄 | §4 루프로 최대 `maxPlaysPerTurn`장 처리, 매 장마다 카드수 UI 갱신·승리 체크 |
| `aiThinkDelay` | 턴당 1회 대기 | 첫 장 전 대기 + 장 사이 `aiPlayInterval` 연출 딜레이 |

기타 코어(`FieldManager`, `FieldLine`, `Card`, `Player`)는 **변경 불필요**. 전략은 읽기 전용 정보(`FieldManager` 조회 API, `hand`, `ctx`)만 사용.

> `AiContext`는 `PlaysThisTurn`(첫 장/추가 장 구분)을 전달하는 용도. 상대 손패 수 필드도 채워두지만(향후 확장용) 현재 승률 무관이라 사용하지 않는다.

### 6.1 로비 → 게임 씬 난이도 전달 (구현됨)
로비와 게임은 별도 씬이라 인스펙터 값이 공유되지 않으므로, `AiDifficultySettings`(static + `PlayerPrefs`)로 보존한다. 로비에서 선택 시 기록 → `CardGameManager.Start()`가 읽어 `CreateStrategy()`에 사용.

**UX 흐름:** 게임하기 → 난이도 팝업(쉬움/보통/어려움) → 시작 → 카드게임.
- `LobbyUI`: `OnGameStart()`가 `difficultyPanel`을 띄우고, 팝업의 `SetDifficulty(i)`로 선택·강조, `시작` 버튼이 `GoToCardGame()` 호출.
- 팝업 UI는 코드로 생성: 메뉴 **Tools ▸ Add Difficulty Popup to Lobby** (`LobbyDifficultySetup.cs`) 실행 시 기존 Lobby 씬에 팝업을 만들고 `LobbyUI` 필드를 자동 연결(재실행 안전). 기존 `LobbySceneSetup` 패턴을 따름.

---

## 7. 규칙 확정 사항 (합의 완료)

1. **패스 페널티 없음.** 보류 한도·벌점 메커니즘은 도입하지 않는다. `PassCount`는 통계 기록 용도로만 유지.
2. **AI도 사람과 동일 규칙.** 낼 수 있는 카드가 있으면 첫 1장은 의무 제출(첫 장 패스/보류 불가), 2·3번째 장만 선택. → §4 루프의 가드(`plays==0 && HasAnyPlayable`)로 코드에서 강제.
3. **난이도는 로비에서 사용자가 선택.** 로비 UI에 Easy/Normal/Hard 선택을 두고, 선택값을 게임 씬(`CardGameManager.aiDifficulty`)으로 전달한다.

---

## 8. 구현 로드맵 (완료)

1. ✅ `RunAITurn`을 루프로 변경(+첫 장 의무 제출 가드) → 턴당 최대 3장(공정성).
2. ✅ `AiDifficulty` enum/설정 홀더 + 팩토리.
3. ✅ `AiContext`(PlaysThisTurn) 주입 경로.
4. ✅ 시뮬레이션으로 전략 검증 → 점수 휴리스틱 폐기, 놓침 확률 모델로 전환.
5. ✅ 로비 난이도 선택 UI → `PlayerPrefs` → 게임 씬 연결.

---

## 9. 검증 결과 (AI vs AI 시뮬레이션)

규칙을 그대로 옮긴 Python 시뮬레이터(`docs/ai_sim.py`)로 검증. 각 4000~6000판, 결정적 시드.

**(1) 카드 선택은 승률 무관** — seat0 vs 무작위 상대 3명:

| seat0 전략 | 승률 |
|------------|------|
| 무작위 | 25.9% |
| 처리량 최대화(DFS-MAX) | 25.9% |
| 처리량 최소화(DFS-MIN) | 25.9% |
| **1장만 내고 멈춤** | **6.2%** ← 유일하게 큰 영향 |

**(2) 난이도 구배** — 사람 대역(완벽) 1명 vs 상대 3명(해당 난이도):

| 상대 난이도 | 사람 승률 |
|-------------|-----------|
| Easy(놓침 0.55) | **35.9%** |
| Normal(0.20) | **30.0%** |
| Hard(0.0, 완벽) | **25.0%** (공정 몫 = 이론적 하한) |

→ Easy↔Hard 사이 사람 승률이 ~11%p 벌어져 체감 가능. Hard는 완벽 플레이라 그 이상 어렵게 만들 수 없음.

**추가 권장 검증**(인게임): 루프가 3장 제한·승리 즉시 종료를 지키는지, Easy AI가 첫 장은 반드시 내는지 in-editor 확인.

---

## 10. GitHub 반영 방안

- 브랜치: `feature/ai-strategy`.
- 커밋: 코어 전략/난이도, 로비 연동, 설계 문서·시뮬레이터를 함께 커밋.
- 본 문서와 `docs/ai_sim.py`(검증 근거)를 함께 둬 졸업작품 기록으로 남김.

---

## 부록 A. 영향 범위 한눈에

| 파일 | 변경 |
|------|------|
| `GameManager.cs` | RunAITurn 루프화·의무 제출 가드, 난이도 enum·팩토리, AiContext 주입 |
| `IAiStrategy.cs` | `ChooseCard`에 `AiContext ctx` 파라미터 추가 |
| `RandomAiStrategy.cs` | `overlookChance` 파라미터 추가(난이도 레버) |
| `AiDifficulty.cs` | **신규** — enum + `AiDifficultySettings`(PlayerPrefs 홀더) |
| `AiContext.cs` | **신규** — PlaysThisTurn 등 턴 컨텍스트 |
| `LobbyUI.cs` | 난이도 팝업 흐름(게임하기→팝업→시작) + `SetDifficulty()`/하이라이트 |
| `Editor/LobbyDifficultySetup.cs` | **신규** — 기존 Lobby 씬에 난이도 팝업 생성·자동 연결하는 메뉴 도구 |
| `GreedyAiStrategy.cs` / `HeuristicAiStrategy.cs` | (작성 후) **폐기** — 효과 없음으로 삭제 |
| `FieldManager / FieldLine / Card / Player` | 변경 없음 |

---

## 10. 정정 (2026-10-22) — 봉쇄 전략과 난이도 재설계

§3의 결론("카드 선택은 승률 무관, 다 내는 게 최선")은 **'무작위로 멈추기'만 비교한 결과**였다.
상황을 보고 골라서 쥐는 전략은 시험하지 않았다. `docs/block_sim.py` 로 다시 검증했다(15,000판).

**봉쇄 전략** (`BlockadeAiStrategy.cs`) — 자기 손패와 필드만 본다(48장이 모두 분배되므로 내 것이 아닌 카드는 반드시 상대 손에 있다).
- 안전한 카드 = 내도 상대에게 새 카드를 열어 주지 않는 카드(다음 장이 내 것이거나 A/K 끝).
- 이번 턴에 손패를 다 비울 수 있으면 비운다.
- 추가 장: 안전한 카드만 낸다. 없으면 쥐고 턴 종료.
- 첫 장(의무): 안전한 카드 중 내 연속이 긴 것. 없으면 그 방향에 내 카드가 많이 남은 줄을 연다.

| 1명만 전략을 바꿈 (나머지 3명 = 다 내기) | 승률 |
|---|---|
| 다 내기 | 25.0% |
| 1장만 내고 멈춤 | 6.3% |
| 고르기만 영리하게(보류 X) | 28.5% |
| 보류만(고르기 X) | 33.6% |
| **봉쇄(고르기 + 보류)** | **80.6%** |

**난이도 재설계** (`CardGameManager.CreateStrategy`)

| 난이도 | 전략 | 사람 승률 (사람 다 내기 / 사람 봉쇄) |
|---|---|---|
| Easy (살살) | RandomAiStrategy 0.20 | 30.6% / 79.5% |
| Normal (보통) | RandomAiStrategy 0.0 | 24.7% / 80.6% |
| Hard (진심) | BlockadeAiStrategy | 8.6% / 21.6% |

C# 구현은 실제 코어 파일(Card·FieldLine·RandomAiStrategy)과 함께 .NET 으로 컴파일해 같은 규칙으로 15,000판 돌려
파이썬과 같은 결과(봉쇄 81.3%, Hard 상대 사람 8.5%)와 규칙 위반 0회를 확인했다.
