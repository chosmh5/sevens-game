using System.Collections.Generic;

/// <summary>
/// AI 의사결정에 필요한 턴 컨텍스트.
/// IAiStrategy 시그니처를 단순하게 유지하기 위해, GameManager가 매 호출 직전에 채워서 전달한다.
/// (전략은 읽기만 한다.)
/// </summary>
public class AiContext
{
    /// <summary>현재 행동 중인 AI의 좌석 인덱스(0~3).</summary>
    public int SelfIndex;

    /// <summary>이번 턴에 이미 낸 카드 수(0이면 첫 장 = 의무 제출 차례).</summary>
    public int PlaysThisTurn;

    /// <summary>한 턴 최대 제출 수.</summary>
    public int MaxPlaysPerTurn;

    /// <summary>나를 제외한 다른 플레이어들의 남은 손패 수.</summary>
    public readonly List<int> OpponentHandCounts = new List<int>();

    /// <summary>상대 중 가장 적은 손패 수(가장 위협적인 상대). 상대가 없으면 int.MaxValue.</summary>
    public int MinOpponentHandCount
    {
        get
        {
            int min = int.MaxValue;
            for (int i = 0; i < OpponentHandCounts.Count; i++)
                if (OpponentHandCounts[i] < min) min = OpponentHandCounts[i];
            return min;
        }
    }
}
