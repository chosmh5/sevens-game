using System;
using System.Collections.Generic;

/// <summary>
/// 게임 내 한 명의 플레이어 (사람 또는 AI).
///
/// 주의: 사람 플레이어(IsAI=false)의 손패는 HandManager가 소유하며,
/// 이 클래스의 Hand는 사용하지 않을 수 있다. AI 플레이어의 손패만 여기에 저장된다.
/// GameManager는 IsAI 여부에 따라 Hand 출처를 결정한다.
/// </summary>
[Serializable]
public class Player
{
    public string Name;
    public bool   IsAI;
    public int    Index;        // 좌석 인덱스(0~3). 0 = 사람.
    public int    PassCount;    // 누적 패스 횟수 (옵션 페널티용)

    /// <summary>AI 손패. 사람 플레이어는 null/비어있을 수 있음.</summary>
    public List<Card> Hand = new List<Card>();

    public Player(int index, string name, bool isAI)
    {
        Index = index;
        Name  = name;
        IsAI  = isAI;
    }

    public override string ToString() => $"[{Index}] {Name}{(IsAI ? "(AI)" : "(나)")}";
}
