using UnityEngine;

/// <summary>
/// AI 난이도. 로비에서 사용자가 선택한다.
/// 이 게임은 카드 선택이 승률에 무관하므로(시뮬레이션 검증), 난이도는
/// RandomAiStrategy의 '추가 장 놓침 확률(overlookChance)'로 구현된다.
/// - Easy   : 자주 놓침(0.55) → 사람이 이기기 쉬움
/// - Normal : 가끔 놓침(0.20)
/// - Hard   : 절대 안 놓침(0.0, 완벽 — 승률 상한 ~25%)
/// 매핑은 CardGameManager.CreateStrategy() 참조.
/// </summary>
public enum AiDifficulty
{
    Easy   = 0,
    Normal = 1,
    Hard   = 2,
}

/// <summary>
/// 로비 → 게임 씬으로 난이도 선택값을 넘기기 위한 경량 홀더.
/// 씬이 분리돼 있어 인스펙터 값이 공유되지 않으므로 static + PlayerPrefs로 보존한다.
/// </summary>
public static class AiDifficultySettings
{
    private const string PrefKey = "sevens.ai.difficulty";

    private static AiDifficulty _selected = AiDifficulty.Normal;
    private static bool _loaded;

    /// <summary>현재 선택된 난이도. 최초 접근 시 PlayerPrefs에서 복원.</summary>
    public static AiDifficulty Selected
    {
        get
        {
            if (!_loaded)
            {
                _selected = (AiDifficulty)PlayerPrefs.GetInt(PrefKey, (int)AiDifficulty.Normal);
                _loaded   = true