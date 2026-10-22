using UnityEngine;

/// <summary>
/// AI 난이도. 홈에서 사용자가 선택한다. (docs/block_sim.py 로 검증)
/// - Easy   : RandomAiStrategy(0.20) — 추가 장을 가끔 무작위로 멈춤
/// - Normal : RandomAiStrategy(0.0)  — 낼 수 있는 만큼 다 냄
/// - Hard   : BlockadeAiStrategy     — 상대에게만 길을 열어 주는 카드는 쥐고 버팀(봉쇄)
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
                _loaded   = true;
            }
            return _selected;
        }
        set
        {
            _selected = value;
            _loaded   = true;
            PlayerPrefs.SetInt(PrefKey, (int)value);
            PlayerPrefs.Save();
        }
    }
}
