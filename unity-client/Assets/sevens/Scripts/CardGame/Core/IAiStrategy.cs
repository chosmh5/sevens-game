using System.Collections.Generic;

/// <summary>
/// AI 카드 선택 전략 인터페이스.
/// </summary>
public interface IAiStrategy
{
    /// <summary>
    /// 손패에서 낼 카드 한 장 선택. 낼 수 있는 카드가 없으면 null 반환(= 패스).
    ///
    /// GameManager가 한 턴 동안 이 메서드를 최대 MaxPlaysPerTurn번