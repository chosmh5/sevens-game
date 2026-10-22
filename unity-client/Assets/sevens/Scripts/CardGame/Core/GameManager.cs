using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 세븐 카드게임의 라운드 진행을 관리.
/// - 4인 플레이어(나 + AI 3) 생성, 12장씩 분배, 4장의 7 자동 세팅
/// - 턴 순환 (0 → 1 → 2 → 3 → 0 …)
/// - 사양: 한 턴에 최대 maxPlaysPerTurn(=3)장까지 낼 수 있다.
///   * 0장 낸 상태에서 낼 카드 없음 = "패스" 버튼 활성
///   * 1~2장 낸 상태 = "턴 종료" 버튼 활성 (전략적으로 더 안 내고 끝낼 수 있음)
///   * 3장 도달 = 자동 턴 종료
/// - AI도 사람과 동일 규칙: 낼 카드가 있으면 첫 장 의무 제출, 2·3번째 장은 전략에 따라 선택.
///   난이도(Easy/Normal/Hard)에 따라 IAiStrategy 구현이 교체된다.
/// - 손패 0장 도달 시 승리, GameOver 상태로 전환.
/// </summary>
public class CardGameManager : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private FieldManager fieldManager;
    [SerializeField] private HandManager  handManager;
    [SerializeField] private Button       passButton;
    [SerializeField] private TMP_Text     passButtonLabel; // "패스" ↔ "턴 종료" 라벨 전환용
    [Tooltip("이번 턴에 낸 카드를 한 장씩 취소(되돌리기). 턴 종료 전까지만 가능.")]
    [SerializeField] private Button       undoButton;

    [Header("Opponent Profiles")]
    [Tooltip("우측(상대 1), 상단(상대 2), 좌측(상대 3) 순서로 GameManager가 자동으로 매핑.")]
    [SerializeField] private OpponentProfile rightProfile; // = AI player 1
    [SerializeField] private OpponentProfile topProfile;   // = AI player 2
    [SerializeField] private OpponentProfile leftProfile;  // = AI player 3

    [Header("Settings")]
    [SerializeField] private int    handSize        = 12;
    [SerializeField] private int    maxPlaysPerTurn = 3;  // 사양: 턴당 최대 3장
    [SerializeField] private float  aiThinkDelay    = 0.8f;
    [SerializeField] private float  aiPlayInterval  = 0.35f; // 한 턴 내 카드 사이 연출 딜레이
    [SerializeField] private int    rngSeed         = 0; // 0 = 시스템 랜덤
    [SerializeField] private bool   runOnStart      = true;

    [Header("AI")]
    [Tooltip("Inspector 기본 난이도. useLobbyDifficulty=true면 로비에서 선택한 값을 우선 사용.")]
    [SerializeField] private AiDifficulty aiDifficulty       = AiDifficulty.Normal;
    [SerializeField] private bool         useLobbyDifficulty = true;
    [SerializeField] private string playerName      = "플레이어";
    [SerializeField] private string[] aiNames       = { "상대 1", "상대 2", "상대 3" };
    [Tooltip("켜면 AI 이름을 마이룸 캐릭터(세비-호/류/진)로 바꾼다. 하이라이트 반응·결과 반응이 이 이름으로 나온다.")]
    [SerializeField] private bool   useCharacterNames = true;

    /// <summary>오른쪽·위·왼쪽 AI 좌석에 앉는 캐릭터.</summary>
    public static readonly string[] CharacterNames = { "세비-호", "류", "진" };

    [Header("Button Labels")]
    [SerializeField] private string passLabel    = "패스";
    [SerializeField] private string endTurnLabel = "턴 종료";

    public enum State { Idle, Dealing, PlayerTurn, AITurn, GameOver }

    // ─────────────────────────────────────────────────────────────

    private Player[] _players;
    private int      _currentIdx;
    private int      _playsThisTurn;   // 사람 턴에서 이번 턴 동안 낸 카드 수 (0~maxPlaysPerTurn)
    private State    _state = State.Idle;
    private IAiStrategy _ai;
    private System.Random _rng;
    private OpponentProfile[] _aiProfiles; // index 0..2 → AI player 1..3
    private readonly AiContext _aiCtx = new AiContext(); // 매 AI 호출 직전 재사용
    private readonly Stack<Card> _pendingPlays = new Stack<Card>(); // 이번 턴 사람이 낸 카드(취소용)
    private readonly List<Card>  _turnPlayed   = new List<Card>();  // 이번 턴에 확정적으로 낸 카드(턴 종료 이벤트용)
    private float _holdUntil;                                        // 이 시각까지 AI 제출을 잠깐 멈춤(연출용)

    public State CurrentState => _state;
    public Player CurrentPlayer => (_players != null && _currentIdx < _players.Length) ? _players[_currentIdx] : null;

    public event Action<Player>       TurnStarted;
    public event Action<Player, Card> PlayerPlayedCard;
    public event Action<Player>       PlayerPassed;
    public event Action<Player>       GameWon;
    /// <summary>새 판이 시작됨(패 분배 직후).</summary>
    public event Action               GameStarted;
    /// <summary>
    /// 한 턴이 확정됨(다음 사람으로 넘어가기 직전). 두 번째 인자는 이번 턴에 낸 카드(패스면 빈 목록).
    /// 사람의 '되돌리기'가 반영된 최종 결과라 하이라이트 판단에 쓴다. 승리로 끝난 턴은 GameWon 만 발생.
    /// </summary>
    public event Action<Player, IReadOnlyList<Card>> TurnEnded;

    /// <summary>좌석 수(4).</summary>
    public int PlayerCount => _players != null ? _players.Length : 0;
    public int MaxPlaysPerTurn => maxPlaysPerTurn;

    /// <summary>사람 턴에서 이번 턴에 낸 카드 수(되돌리기 반영).</summary>
    public int PlaysThisTurn => _playsThisTurn;

    /// <summary>턴 버튼 상태가 다시 계산될 때마다(턴 시작·제출·되돌리기·종료) 발생. HUD 갱신용.</summary>
    public event Action TurnStateChanged;

    /// <summary>되돌리기 버튼을 런타임에 연결(씬에 버튼이 없을 때 레트로 스킨이 만들어 넘긴다).</summary>
    public void BindUndoButton(Button button)
    {
        if (undoButton != null) undoButton.onClick.RemoveListener(OnUndoClicked);
        undoButton = button;
        if (undoButton != null) undoButton.onClick.AddListener(OnUndoClicked);
        RefreshTurnButton();
    }

    /// <summary>좌석별 남은 손패 수. 0번(사람)은 HandManager 기준.</summary>
    public int[] GetHandCounts()
    {
        if (_players == null) return new int[0];
        var counts = new int[_players.Length];
        for (int i = 0; i < _players.Length; i++)
            counts[i] = (i == 0 && handManager != null) ? handManager.Hand.Count : _players[i].Hand.Count;
        return counts;
    }

    /// <summary>좌석의 현재 손패(읽기 전용). 0번(사람)은 HandManager 기준. 하이라이트 '봉쇄' 판단용.</summary>
    public IReadOnlyList<Card> GetHand(int seat)
    {
        if (_players == null || seat < 0 || seat >= _players.Length) return null;
        return seat == 0 && handManager != null ? handManager.Hand : _players[seat].Hand;
    }

    public string GetPlayerName(int seat) =>
        (_players != null && seat >= 0 && seat < _players.Length) ? _players[seat].Name : "";

    public int GetPassCount(int seat) =>
        (_players != null && seat >= 0 && seat < _players.Length) ? _players[seat].PassCount : 0;

    /// <summary>AI 좌석(1~3)의 프로필 UI. 사람(0)이나 범위 밖이면 null.</summary>
    public OpponentProfile GetProfile(int seat) =>
        (_aiProfiles != null && seat >= 1 && seat - 1 < _aiProfiles.Length) ? _aiProfiles[seat - 1] : null;

    /// <summary>연출을 보여줄 시간 동안 AI가 다음 카드를 내지 않고 기다리게 한다.</summary>
    public void HoldAI(float seconds)
    {
        _holdUntil = Mathf.Max(_holdUntil, Time.time + seconds);
    }

    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        _aiProfiles = new[] { rightProfile, topProfile, leftProfile };

        AiDifficulty diff = useLobbyDifficulty ? AiDifficultySettings.Selected : aiDifficulty;
        _ai  = CreateStrategy(diff, rngSeed);
        _rng = rngSeed == 0 ? new System.Random() : new System.Random(rngSeed);
        Debug.Log($"[GameManager] AI 난이도: {diff}");

        if (passButton != null) passButton.onClick.AddListener(OnPassClicked);
        if (undoButton != null)
        {
            undoButton.onClick.RemoveListener(OnUndoClicked); // BindUndoButton 으로 먼저 연결됐을 수 있음
            undoButton.onClick.AddListener(OnUndoClicked);
        }
        if (handManager != null) handManager.CardSubmitted += OnHumanCardPlayed;

        if (runOnStart) StartNewGame();
    }

    private void OnDestroy()
    {
        if (passButton  != null) passButton.onClick.RemoveListener(OnPassClicked);
        if (undoButton  != null) undoButton.onClick.RemoveListener(OnUndoClicked);
        if (handManager != null) handManager.CardSubmitted -= OnHumanCardPlayed;
    }

    // ─────────────────────────────────────────────────────────────
    //  Game lifecycle
    // ─────────────────────────────────────────────────────────────

    [ContextMenu("▶ Start New Game")]
    public void StartNewGame()
    {
        if (fieldManager == null || handManager == null)
        {
            Debug.LogError("[GameManager] FieldManager / HandManager 참조가 비어있습니다.");
            return;
        }

        _state = State.Dealing;

        // 플레이어 생성
        _players = new Player[4];
        _players[0] = new Player(0, playerName, false);
        for (int i = 1; i < 4; i++)
        {
            string[] names = useCharacterNames ? CharacterNames : aiNames;
            _players[i] = new Player(i, (i - 1) < names.Length ? names[i - 1] : $"AI {i}", true);
        }

        // 필드 리셋 + 7 자동 세팅
        fieldManager.ResetField();
        fieldManager.PlaceInitialSevens();

        // 7 제외 48장 셔플 후 12장씩 분배
        var deck = BuildDeckExcludingSevens();
        Shuffle(deck);

        int deckIdx = 0;
        for (int p = 0; p < 4; p++)
        {
            _players[p].Hand.Clear();
            for (int c = 0; c < handSize && deckIdx < deck.Count; c++)
                _players[p].Hand.Add(deck[deckIdx++]);

            // 보기 좋게 정렬
            _players[p].Hand.Sort((a, b) =>
            {
                int s = a.Suit.CompareTo(b.Suit);
                return s != 0 ? s : a.Number.CompareTo(b.Number);
            });
        }

        // 사람 손패는 HandManager에 위임
        handManager.SetInitialHand(_players[0].Hand);
        _players[0].Hand.Clear(); // 이제 HandManager가 단일 소스 오브 트루스

        // AI 프로필 바인딩
        for (int i = 0; i < _aiProfiles.Length; i++)
            _aiProfiles[i]?.Bind(_players[i + 1]);

        Debug.Log("[GameManager] 새 게임 시작. 필드: " + fieldManager.DescribeState());
        for (int i = 0; i < 4; i++)
            Debug.Log($"[GameManager] {_players[i]}: {HandPreview(i)}");

        _currentIdx = 0;
        _holdUntil = 0f;
        GameStarted?.Invoke();
        BeginTurn();
    }

    private void BeginTurn()
    {
        if (_state == State.GameOver) return;

        var p = _players[_currentIdx];
        _playsThisTurn = 0;       // 새 턴 시작 — 카운터 리셋
        _pendingPlays.Clear();    // 이전 턴 제출은 확정됨 — 취소 스택 비움
        _turnPlayed.Clear();

        // 프로필 강조 갱신
        for (int i = 0; i < _aiProfiles.Length; i++)
            _aiProfiles[i]?.SetTurnActive(false);
        if (_currentIdx != 0 && (_currentIdx - 1) < _aiProfiles.Length)
            _aiProfiles[_currentIdx - 1]?.SetTurnActive(true);

        TurnStarted?.Invoke(p);

        if (p.IsAI)
        {
            _state = State.AITurn;
            handManager.SetInteractable(false); // 상대 턴: 내 손패 입력 차단
            RefreshTurnButton();
            StartCoroutine(RunAITurn(p));
        }
        else
        {
            _state = State.PlayerTurn;
            handManager.SetInteractable(true);  // 내 턴: 입력 허용 + 가용 카드 표시
            RefreshTurnButton();
            Debug.Log("[GameManager] 당신의 턴입니다.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Human turn
    // ─────────────────────────────────────────────────────────────

    private void OnHumanCardPlayed(Card card)
    {
        if (_state != State.PlayerTurn) return;

        // HandManager가 이미 fieldManager.TryPlace 호출 + 자기 손에서 제거 완료.
        _playsThisTurn++;
        _pendingPlays.Push(card); // 취소 가능하도록 기록
        _turnPlayed.Add(card);
        PlayerPlayedCard?.Invoke(_players[0], card);
        Debug.Log($"[GameManager] {_players[0].Name} → {card} ({_playsThisTurn}/{maxPlaysPerTurn})");

        if (handManager.Hand.Count == 0)
        {
            FinishGame(_players[0]);
            return;
        }

        // 사양: 한 턴에 최대 maxPlaysPerTurn(=3)장.
        // 도달해도 자동 종료하지 않고, 사람이 "턴 종료" 버튼을 직접 눌러야 넘어간다.
        if (_playsThisTurn >= maxPlaysPerTurn)
        {
            Debug.Log($"[GameManager] {_players[0].Name} {maxPlaysPerTurn}장 모두 냄 — \"턴 종료\" 버튼 대기");
            handManager.SetHandLocked(true); // 더 못 내도록 손패 잠금
            RefreshTurnButton();             // "턴 종료" 활성 상태로
            return;
        }

        // 아직 더 낼 수 있음. 사람이 다음 카드 클릭 또는 "턴 종료" 누를 때까지 대기.
        handManager.RefreshPlayableStates();
        RefreshTurnButton();
    }

    /// <summary>
    /// 패스/턴 종료 버튼 클릭.
    /// - 0장 냄 + 낼 카드 없음 = 패스
    /// - 1장 이상 냄 = 자발적 턴 종료
    /// (낼 카드가 있는데 0장 낸 상태에서는 버튼이 비활성이라 여기 안 들어옴)
    /// </summary>
    private void OnPassClicked()
    {
        if (_state != State.PlayerTurn) return;

        if (_playsThisTurn == 0)
        {
            // 패스: 정말 낼 카드 없는지 방어 체크
            if (fieldManager.HasAnyPlayable(handManager.Hand))
            {
                Debug.LogWarning("[GameManager] 낼 수 있는 카드가 있는데 패스 호출됨 — 무시.");
                return;
            }
            _players[0].PassCount++;
            PlayerPassed?.Invoke(_players[0]);
            Debug.Log($"[GameManager] {_players[0].Name} 패스");
        }
        else
        {
            Debug.Log($"[GameManager] {_players[0].Name} 턴 종료 ({_playsThisTurn}장 냄)");
        }

        NextTurn();
    }

    /// <summary>
    /// 취소(되돌리기) 버튼 클릭. 이번 턴에 낸 카드를 한 장씩 되돌린다.
    /// 필드에서 제거 → 손패로 복귀 → 카운터 감소. 턴 종료 전까지만 가능.
    /// </summary>
    private void OnUndoClicked()
    {
        if (_state != State.PlayerTurn) return;
        if (_pendingPlays.Count == 0) return;

        Card card = _pendingPlays.Pop();
        if (!fieldManager.UndoPlace(card))
        {
            // 되돌리기 실패(예상치 못한 상태) — 스택 복원
            _pendingPlays.Push(card);
            Debug.LogWarning($"[GameManager] {card} 되돌리기 실패 — 무시.");
            return;
        }

        handManager.ReturnCard(card); // 손패로 복귀(+ 잠금 해제/가용성 갱신)
        _playsThisTurn--;
        _turnPlayed.Remove(card);
        Debug.Log($"[GameManager] {_players[0].Name} 취소 ← {card} ({_playsThisTurn}/{maxPlaysPerTurn})");

        RefreshTurnButton();
    }

    /// <summary>
    /// 버튼 상태/라벨 결정:
    /// 기본은 항상 "턴 종료". 단, 낼 카드 자체가 없는 상태에서만 "패스"로 바뀐다.
    ///
    ///   - 사람 턴 아님          → 비활성
    ///   - 0장 냄 + 낼 카드 있음 → "턴 종료" 비활성 (반드시 1장은 내야 함)
    ///   - 0장 냄 + 낼 카드 없음 → "패스"  활성   (어쩔 수 없이 넘어감)
    ///   - 1장 이상 냄           → "턴 종료" 활성
    /// </summary>
    private void RefreshTurnButton()
    {
        RefreshTurnButtonCore();
        try { TurnStateChanged?.Invoke(); }
        catch (Exception e) { Debug.LogException(e); }
    }

    private void RefreshTurnButtonCore()
    {
        // 취소 버튼: 내 턴이고 이번 턴에 낸 카드가 있을 때만 활성.
        if (undoButton != null)
            undoButton.interactable = (_state == State.PlayerTurn && _pendingPlays.Count > 0);

        if (passButton == null) return;

        if (_state != State.PlayerTurn)
        {
            passButton.interactable = false;
            SetTurnButtonLabel(endTurnLabel);
            return;
        }

        bool hasPlayable = fieldManager.HasAnyPlayable(handManager.Hand);

        if (_playsThisTurn == 0 && !hasPlayable)
        {
            // 첫 행동 전이면서 낼 카드가 전혀 없는 경우만 "패스".
            passButton.interactable = true;
            SetTurnButtonLabel(passLabel);
        }
        else
        {
            // 그 외 모든 경우는 "턴 종료" 라벨.
            // 0장 냈고 낼 카드 있으면 비활성(반드시 한 장 내야 함),
            // 1장 이상 냈으면 활성.
            passButton.interactable = _playsThisTurn > 0;
            SetTurnButtonLabel(endTurnLabel);
        }
    }

    private void SetTurnButtonLabel(string text)
    {
        if (passButtonLabel != null) passButtonLabel.text = text;
    }

    // ─────────────────────────────────────────────────────────────
    //  AI turn
    // ─────────────────────────────────────────────────────────────

    private IEnumerator RunAITurn(Player p)
    {
        yield return new WaitForSeconds(aiThinkDelay);

        int plays = 0;

        // 사람과 동일 규칙: 낼 카드가 있으면 첫 장은 의무 제출, 이후 최대 maxPlaysPerTurn까지 선택 제출.
        while (plays < maxPlaysPerTurn)
        {
            // 하이라이트 연출 중이면 잠깐 기다린다.
            while (Time.time < _holdUntil) yield return null;

            FillAiContext(p, plays);
            Card chosen = _ai.ChooseCard(fieldManager, p.Hand, _aiCtx);

            // 의무 제출 가드: 첫 장에서 낼 카드가 있는데 전략이 null을 주면 강제로 1장 선택.
            if (chosen == null && plays == 0 && fieldManager.HasAnyPlayable(p.Hand))
            {
                var playable = fieldManager.FilterPlayable(p.Hand);
                chosen = playable[0];
            }

            if (chosen == null) break;                       // plays>=1에서 null = 자발적 턴 종료
            if (!fieldManager.TryPlace(chosen)) break;        // 방어: 못 놓으면 종료

            p.Hand.Remove(chosen);
            plays++;
            _turnPlayed.Add(chosen);
            PlayerPlayedCard?.Invoke(p, chosen);
            Debug.Log($"[GameManager] {p.Name} → {chosen} ({plays}/{maxPlaysPerTurn})");

            // 카드 수 / 손패 가용성 즉시 갱신
            for (int i = 0; i < _aiProfiles.Length; i++)
                _aiProfiles[i]?.SetCardCount(_players[i + 1].Hand.Count);
            handManager.RefreshPlayableStates();

            if (p.Hand.Count == 0)
            {
                FinishGame(p);
                yield break;
            }

            yield return new WaitForSeconds(aiPlayInterval); // 연속 제출 연출
        }

        if (plays == 0) // 낼 카드가 정말 없었던 경우만 패스
        {
            p.PassCount++;
            PlayerPassed?.Invoke(p);
            Debug.Log($"[GameManager] {p.Name} 패스");
        }

        handManager.RefreshPlayableStates();
        NextTurn();
    }

    /// <summary>AI 호출 직전 컨텍스트 갱신(상대 손패 수, 이번 턴 제출 수).</summary>
    private void FillAiContext(Player self, int playsThisTurn)
    {
        _aiCtx.SelfIndex       = self.Index;
        _aiCtx.PlaysThisTurn   = playsThisTurn;
        _aiCtx.MaxPlaysPerTurn = maxPlaysPerTurn;

        _aiCtx.OpponentHandCounts.Clear();
        for (int i = 0; i < _players.Length; i++)
        {
            if (i == self.Index) continue;
            // 사람(0번)의 손패는 HandManager가 소유.
            int count = (i == 0) ? handManager.Hand.Count : _players[i].Hand.Count;
            _aiCtx.OpponentHandCounts.Add(count);
        }
    }

    /// <summary>
    /// 난이도 → 전략 인스턴스.
    /// 이 게임은 카드 선택이 승률에 무관하므로(시뮬레이션 검증), 난이도는
    /// '추가 장 놓침 확률(overlookChance)'로 구현한다. Hard는 0(완벽).
    /// </summary>
    private static IAiStrategy CreateStrategy(AiDifficulty d, int seed)
    {
        switch (d)
        {
            case AiDifficulty.Easy:   return new RandomAiStrategy(seed, 0.55f);
            case AiDifficulty.Normal: return new RandomAiStrategy(seed, 0.20f);
            case AiDifficulty.Hard:   return new RandomAiStrategy(seed, 0.00f);
            default:                  return new RandomAiStrategy(seed, 0.20f);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private void NextTurn()
    {
        // 턴 확정 알림(하이라이트 감지용). 구독자가 예외를 내도 게임 진행은 막지 않는다.
        try { TurnEnded?.Invoke(_players[_currentIdx], _turnPlayed.ToArray()); }
        catch (Exception e) { Debug.LogException(e); }

        _currentIdx = (_currentIdx + 1) % _players.Length;
        BeginTurn();
    }

    private void FinishGame(Player winner)
    {
        _state = State.GameOver;
        handManager.SetInteractable(false); // 게임오버: 입력 차단
        for (int i = 0; i < _aiProfiles.Length; i++)
            _aiProfiles[i]?.SetTurnActive(false);
        RefreshTurnButton(); // 버튼 비활성화
        GameWon?.Invoke(winner);
        Debug.Log($"[GameManager] 🏆 승자: {winner.Name}");
    }

    private static List<Card> BuildDeckExcludingSevens()
    {
        var deck = new List<Card>(48);
        foreach (Suit s in Enum.GetValues(typeof(Suit)))
        {
            for (int n = 1; n <= 13; n++)
            {
                if (n == Card.SevenNumber) continue;
                deck.Add(new Card(s, n));
            }
        }
        return deck;
    }

    private void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = _rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private string HandPreview(int idx)
    {
        IReadOnlyList<Card> h = idx == 0 ? handManager.Hand : _players[idx].Hand;
        if (h == null || h.Count == 0) return "(empty)";
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < h.Count; i++)
        {
            sb.Append(h[i]);
            if (i < h.Count - 1) sb.Append(' ');
        }
        return sb.ToString();
    }
}
