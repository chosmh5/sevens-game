using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FieldManager 동작 검증용 셀프 테스트.
///
/// 사용법:
///   1) 빈 GameObject를 만들고 FieldManager와 이 컴포넌트를 함께 부착.
///   2) 컴포넌트 상단 ⋮ 메뉴 또는 인스펙터 컨텍스트 메뉴에서 "Run All Tests" 실행.
///   3) Console 로그로 결과 확인.
///
/// 정식 EditMode 테스트가 아니므로, 향후 asmdef를 추가하면
/// 같은 케이스를 NUnit으로 옮겨 자동화하는 것을 권장.
/// </summary>
[RequireComponent(typeof(FieldManager))]
public class FieldManagerSelfTest : MonoBehaviour
{
    private FieldManager _field;
    private int _passed;
    private int _failed;
    private readonly List<string> _failures = new List<string>();

    private FieldManager Field
    {
        get
        {
            if (_field == null) _field = GetComponent<FieldManager>();
            return _field;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Entry points (인스펙터 컨텍스트 메뉴)
    // ─────────────────────────────────────────────────────────────

    [ContextMenu("▶ Run All Tests")]
    public void RunAllTests()
    {
        ResetCounters();

        Test_InitialState_AllClosed();
        Test_PlaceInitialSevens_OpensAllLines();
        Test_CannotPlaceSevenOnClosedLine_AfterAlreadyOpened();
        Test_ExtendUpFromSeven();
        Test_ExtendDownFromSeven();
        Test_CannotSkipNumber();
        Test_CannotPlaceWrongSuit();
        Test_BoundaryAce();
        Test_BoundaryKing();
        Test_HasAnyPlayable_TrueAndFalseCases();
        Test_FilterPlayable_ReturnsExactlyValid();
        Test_ResetField_BackToInitial();
        Test_FullChain_AceToKing_OnSpade();

        ReportSummary();
    }

    [ContextMenu("Reset Field")]
    public void ResetFieldOnly()
    {
        Field.ResetField();
        Debug.Log("[SelfTest] Field reset. " + Field.DescribeState());
    }

    [ContextMenu("Place Initial Sevens")]
    public void PlaceSevensOnly()
    {
        Field.PlaceInitialSevens();
        Debug.Log("[SelfTest] Sevens placed. " + Field.DescribeState());
    }

    // ─────────────────────────────────────────────────────────────
    //  Test cases
    // ─────────────────────────────────────────────────────────────

    private void Test_InitialState_AllClosed()
    {
        Field.ResetField();
        bool allClosed = true;
        foreach (var line in Field.AllLines)
            if (line.IsOpen) { allClosed = false; break; }
        Assert(allClosed, nameof(Test_InitialState_AllClosed), "리셋 후 모든 줄이 닫혀 있어야 함");
    }

    private void Test_PlaceInitialSevens_OpensAllLines()
    {
        Field.ResetField();
        Field.PlaceInitialSevens();
        bool allOpen = true;
        foreach (var line in Field.AllLines)
        {
            if (!line.IsOpen || line.MinPlaced != 7 || line.MaxPlaced != 7)
            { allOpen = false; break; }
        }
        Assert(allOpen, nameof(Test_PlaceInitialSevens_OpensAllLines),
            "초기 7 자동 세팅 후 4줄 모두 IsOpen=true & Min=Max=7 이어야 함");
    }

    private void Test_CannotPlaceSevenOnClosedLine_AfterAlreadyOpened()
    {
        // 사양상 7은 손패에 없지만, 방어적으로 한 번 더 7을 내려도 거절되어야 한다.
        Field.ResetField();
        Field.PlaceInitialSevens();
        bool rejected = !Field.CanPlace(new Card(Suit.Spade, 7));
        Assert(rejected, nameof(Test_CannotPlaceSevenOnClosedLine_AfterAlreadyOpened),
            "이미 열린 줄에 같은 문양 7을 또 놓을 수는 없어야 함");
    }

    private void Test_ExtendUpFromSeven()
    {
        Field.ResetField();
        Field.PlaceInitialSevens();
        bool ok = Field.TryPlace(new Card(Suit.Heart, 8))
               && Field.TryPlace(new Card(Suit.Heart, 9))
               && Field.TryPlace(new Card(Suit.Heart, 10));
        var line = Field.GetLine(Suit.Heart);
        Assert(ok && line.MaxPlaced == 10 && line.MinPlaced == 7,
            nameof(Test_ExtendUpFromSeven),
            $"♥ 8,9,10 순서대로 위쪽 확장 가능해야 함 (실제 Max={line.MaxPlaced})");
    }

    private void Test_ExtendDownFromSeven()
    {
        Field.ResetField();
        Field.PlaceInitialSevens();
        bool ok = Field.TryPlace(new Card(Suit.Diamond, 6))
               && Field.TryPlace(new Card(Suit.Diamond, 5))
               && Field.TryPlace(new Card(Suit.Diamond, 4));
        var line = Field.GetLine(Suit.Diamond);
        Assert(ok && line.MinPlaced == 4 && line.MaxPlaced == 7,
            nameof(Test_ExtendDownFromSeven),
            $"♦ 6,5,4 순서대로 아래쪽 확장 가능해야 함 (실제 Min={line.MinPlaced})");
    }

    private void Test_CannotSkipNumber()
    {
        Field.ResetField();
        Field.PlaceInitialSevens();
        // ♣7 → ♣9는 ♣8을 건너뛰므로 불가.
        bool rejected = !Field.CanPlace(new Card(Suit.Club, 9));
        Assert(rejected, nameof(Test_CannotSkipNumber),
            "8을 건너뛰고 9를 바로 놓을 수 없어야 함");
    }

    private void Test_CannotPlaceWrongSuit()
    {
        Field.ResetField();
        Field.PlaceInitialSevens();
        Field.TryPlace(new Card(Suit.Heart, 8));
        // ♥ 라인 끝(8) 옆이라고 해서 ♠9가 ♥ 라인에 들어갈 수는 없다 (FieldManager는 Suit로 라인 결정).
        // 또한 ♠ 라인은 max=7이므로 ♠9는 거리=2라서 불가.
        bool rejected = !Field.CanPlace(new Card(Suit.Spade, 9));
        Assert(rejected, nameof(Test_CannotPlaceWrongSuit),
            "다른 문양 줄의 끝값 옆이라고 해서 자기 줄에 못 놓는 카드를 놓을 수 없어야 함");
    }

    private void Test_BoundaryAce()
    {
        Field.ResetField();
        Field.PlaceInitialSevens();
        // ♠ 6→5→4→3→2→A 까지 내려간 뒤 더 이상 내려갈 수 없어야 함.
        for (int n = 6; n >= 1; n--)
            Field.TryPlace(new Card(Suit.Spade, n));

        var line = Field.GetLine(Suit.Spade);
        bool reachedAce = line.MinPlaced == 1;
        bool noFurther  = !Field.CanPlace(new Card(Suit.Spade, 0)); // 0은 유효 카드도 아니지만 방어 체크
        Assert(reachedAce && noFurther, nameof(Test_BoundaryAce),
            $"A까지 도달 후 더 내려갈 수 없어야 함 (Min={line.MinPlaced})");
    }

    private void Test_BoundaryKing()
    {
        Field.ResetField();
        Field.PlaceInitialSevens();
        for (int n = 8; n <= 13; n++)
            Field.TryPlace(new Card(Suit.Club, n));

        var line = Field.GetLine(Suit.Club);
        bool reachedKing = line.MaxPlaced == 13;
        bool noFurther   = !Field.CanPlace(new Card(Suit.Club, 14));
        Assert(reachedKing && noFurther, nameof(Test_BoundaryKing),
            $"K까지 도달 후 더 올라갈 수 없어야 함 (Max={line.MaxPlaced})");
    }

    private void Test_HasAnyPlayable_TrueAndFalseCases()
    {
        Field.ResetField();
        Field.PlaceInitialSevens();

        var handPlayable = new List<Card> {
            new Card(Suit.Spade, 10), // ♠ 라인 max=7이라 불가 (skip)
            new Card(Suit.Heart, 8),  // ♥ 라인 max=7이라 8 가능 (재미있는 케이스)
        };
        var handStuck = new List<Card> {
            new Card(Suit.Spade, 10),
            new Card(Suit.Heart, 12),
            new Card(Suit.Diamond, 1),
            new Card(Suit.Club, 13),
        };

        bool case1 = Field.HasAnyPlayable(handPlayable) == true;
        bool case2 = Field.HasAnyPlayable(handStuck)    == false;
        Assert(case1 && case2, nameof(Test_HasAnyPlayable_TrueAndFalseCases),
            $"낼 수 있는 손/없는 손 판정이 정확해야 함 (case1={case1}, case2={case2})");
    }

    private void Test_FilterPlayable_ReturnsExactlyValid()
    {
        Field.ResetField();
        Field.PlaceInitialSevens();

        var hand = new List<Card> {
            new Card(Suit.Spade, 6),   // valid (=Spade min-1)
            new Card(Suit.Spade, 8),   // valid (=Spade max+1)
            new Card(Suit.Spade, 10),  // invalid (skip)
            new Card(Suit.Heart, 6),   // valid
            new Card(Suit.Club, 1),    // invalid
        };
        var playable = Field.FilterPlayable(hand);
        bool ok = playable.Count == 3
               && playable.Contains(new Card(Suit.Spade, 6))
               && playable.Contains(new Card(Suit.Spade, 8))
               && playable.Contains(new Card(Suit.Heart, 6));
        Assert(ok, nameof(Test_FilterPlayable_ReturnsExactlyValid),
            $"FilterPlayable 결과가 정확해야 함 (개수={playable.Count})");
    }

    private void Test_ResetField_BackToInitial()
    {
        Field.PlaceInitialSevens();
        Field.TryPlace(new Card(Suit.Heart, 8));
        Field.ResetField();
        bool allClosed = true;
        foreach (var line in Field.AllLines)
            if (line.IsOpen) { allClosed = false; break; }
        Assert(allClosed, nameof(Test_ResetField_BackToInitial),
            "ResetField 호출 시 모든 줄이 다시 닫혀 있어야 함");
    }

    private void Test_FullChain_AceToKing_OnSpade()
    {
        Field.ResetField();
        Field.PlaceInitialSevens();
        // ♠ A부터 K까지 다 놓아본다 (7 제외).
        var seq = new int[] { 6, 5, 4, 3, 2, 1, 8, 9, 10, 11, 12, 13 };
        bool allOk = true;
        foreach (var n in seq)
        {
            if (!Field.TryPlace(new Card(Suit.Spade, n)))
            { allOk = false; break; }
        }
        var line = Field.GetLine(Suit.Spade);
        Assert(allOk && line.MinPlaced == 1 && line.MaxPlaced == 13,
            nameof(Test_FullChain_AceToKing_OnSpade),
            $"♠ 전체 1~13 채울 수 있어야 함 (Min={line.MinPlaced}, Max={line.MaxPlaced})");
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private void ResetCounters()
    {
        _passed = 0;
        _failed = 0;
        _failures.Clear();
    }

    private void Assert(bool condition, string testName, string msg)
    {
        if (condition)
        {
            _passed++;
            Debug.Log($"<color=#5fd35f>PASS</color>  {testName}");
        }
        else
        {
            _failed++;
            string entry = $"<color=#ff6464>FAIL</color>  {testName} — {msg}";
            _failures.Add(entry);
            Debug.LogError(entry);
        }
    }

    private void ReportSummary()
    {
        int total = _passed + _failed;
        string color = _failed == 0 ? "#5fd35f" : "#ff6464";
        Debug.Log($"<color={color}><b>[FieldManager SelfTest] {_passed}/{total} passed, {_failed} failed</b></color>");
    }
}
