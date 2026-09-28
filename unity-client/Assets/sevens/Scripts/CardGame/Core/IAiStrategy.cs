using System.Collections.Generic;

/// <summary>
/// AI 카드 선택 전략 인터페이스.
/// </summary>
public interface IAiStrategy
{
    /// <summary>
    /// 손패에서 낼 카드 한 장 선택. 낼 수 있는 카드가 없으면 null 반환(= 패스).
    ///
    /// GameManager가 한 턴 동안 이 메서드를 최대 MaxPlaysPerTurn번 반복 호출한다.
    /// - ctx.PlaysThisTurn == 0 : 첫 장(낼 카드가 있으면 의무 제출 차례).
    /// - ctx.PlaysThisTurn >= 1 : 추가 장. null을 반환하면 자발적 턴 종료(멈춤).
    /// </summary>
    /// <param name="field">현재 필드 상태(읽기 전용으로 사용).</param>
    /// <param name="hand">이 AI의 현재 손패.</param>
    /// <param name="ctx">턴 컨텍스트(상대 손패 수, 이번 턴 제출 수 등).</param>
    Card ChooseCard(FieldManager field, IReadOnlyList<Card> hand, AiContext ctx);
}
