using System;
using System.Collections.Generic;

/// <summary>
/// 카드게임 하이라이트 때 캐릭터가 하는 한마디.
///
/// - 서버(LLM)가 게임 시작 때 캐릭터·상황별 대사 묶음을 보내 주면 그걸 우선 쓰고(/game/reaction_bank),
/// - 서버가 없거나 아직 만드는 중이면 아래 기본 대사를 쓴다(캐릭터 카드의 말투에 맞춰 직접 씀).
///
/// 키 = "{상황}_{관점}"
///   상황: last_card(마지막 한 장) / comeback(역전) / blockade(플레이어가 카드를 쥐고 막음) / stuck(막힘) / close_race(접전)
///   관점: self(내가) / player(플레이어가) / rival(다른 캐릭터가)
/// </summary>
public static class CharacterReactionLines
{
    private static readonly Dictionary<string, Dictionary<string, string[]>> Defaults =
        new Dictionary<string, Dictionary<string, string[]>>
    {
        ["세비-호"] = new Dictionary<string, string[]>
        {
            ["last_card_self"]   = new[] { "한 장 남았네요. 이변은 없어요.", "마지막 한 장. 역시 저죠." },
            ["last_card_player"] = new[] { "한 장이요? 이번만 운이 좋은 거예요.", "어디 마지막까지 버텨 보세요." },
            ["last_card_rival"]  = new[] { "벌써 한 장이요? 아직 안 끝났어요.", "끝날 때까진 모르는 거죠." },
            ["comeback_self"]    = new[] { "이 정도 차이는 원래 뒤집으라고 있는 거죠.", "역전이요? 계산대로예요." },
            ["comeback_player"]  = new[] { "지금 저를 앞질렀다고요? 우연이에요.", "제법이네요. 아주 조금요." },
            ["comeback_rival"]   = new[] { "흐름이 바뀌었네요. 다시 뺏어 오면 되죠.", "역전이라니, 재밌어지네요." },
            ["blockade_player"]  = new[] { "…패스라니. 이번만 봐주는 거예요.", "막았다고 좋아하지 마요. 다음엔 안 당해요." },
            ["stuck_self"]       = new[] { "패스. 일부러 기다리는 거예요.", "지금은 숨 고르는 거예요." },
            ["stuck_player"]     = new[] { "또 패스예요? 제가 너무 잘 막았나 봐요.", "낼 카드가 없나 봐요? 안됐네요." },
            ["stuck_rival"]      = new[] { "막혔네요. 제 덕분이죠.", "패스 행진이네요." },
            ["close_race"]       = new[] { "이제부터 진짜 승부예요.", "다들 거의 다 왔네요. 재밌어지는데요." },
            ["game_start"]       = new[] { "자, 시작하죠. 1등 자리는 이미 제 거예요.", "오늘도 이기는 건 저예요. 각오해요." },
            ["game_start_talked"] = new[] { "아까 한 말, 기억하고 있어요. 카드로 증명해 보세요.", "얘기는 충분히 했으니까, 이제 실력으로 보여줘요." },
        },
        ["류"] = new Dictionary<string, string[]>
        {
            ["last_card_self"]   = new[] { "한 장 남았어요.", "끝이 보이네요." },
            ["last_card_player"] = new[] { "한 장이네요.", "조심해요. 다들 노려요." },
            ["last_card_rival"]  = new[] { "서두르네요.", "아직 몰라요." },
            ["comeback_self"]    = new[] { "흐름이 왔네요.", "예상대로예요." },
            ["comeback_player"]  = new[] { "뒤집었네요.", "잘 버텼어요." },
            ["comeback_rival"]   = new[] { "흐름이 바뀌었네요.", "그럴 줄 알았어요." },
            ["blockade_player"]  = new[] { "막혔네요.", "한 수 당했네요." },
            ["stuck_self"]       = new[] { "기다릴게요.", "패스요." },
            ["stuck_player"]     = new[] { "막혔네요.", "때를 기다려요." },
            ["stuck_rival"]      = new[] { "보이는 대로네요.", "막혔네요." },
            ["close_race"]       = new[] { "이제 한 수예요.", "집중할 때예요." },
            ["game_start"]       = new[] { "시작하죠.", "집중해요." },
            ["game_start_talked"] = new[] { "아까 얘기, 들었어요.", "말보단 카드죠." },
        },
        ["진"] = new Dictionary<string, string[]>
        {
            ["last_card_self"]   = new[] { "마지막 한 장… 누가 막을 수 있을까요?", "한 장 남았네요… 운명은 이미 정해졌을지도요." },
            ["last_card_player"] = new[] { "한 장 남았네요… 그 카드, 믿어도 될까요?", "끝이 가까워졌네요… 설레요?" },
            ["last_card_rival"]  = new[] { "서두르는 사람이 먼저 넘어지곤 하죠…", "한 장… 과연 그럴까요?" },
            ["comeback_self"]    = new[] { "바람은 늘 방향을 바꾸죠…", "보세요, 흐름은 돌고 도는 거예요…" },
            ["comeback_player"]  = new[] { "역시… 이렇게 될 줄 알았어요.", "재밌는 반전이네요… 다음엔 뭘 보여줄 건가요?" },
            ["comeback_rival"]   = new[] { "판이 뒤집혔네요… 느껴지죠?", "흐름이 바뀌었어요… 누구 덕분일까요?" },
            ["blockade_player"]  = new[] { "길이 닫혔네요… 이것도 흐름이겠죠.", "막혔네요… 오래가진 않을 거예요." },
            ["stuck_self"]       = new[] { "지금은 기다릴 때예요…", "조용히 지켜볼게요…" },
            ["stuck_player"]     = new[] { "길이 막혔나요… 아니면 기다리는 건가요?", "막힌 길 뒤엔 뭐가 있을까요…" },
            ["stuck_rival"]      = new[] { "길이 막혔네요… 누가 막았을까요?", "기다림도 수예요…" },
            ["close_race"]       = new[] { "이제 누가 웃을까요…", "마지막 장면이 다가오네요…" },
            ["game_start"]       = new[] { "이번 판은… 어떤 이야기가 될까요.", "카드가 섞였네요… 운명도요." },
            ["game_start_talked"] = new[] { "아까 나눈 이야기… 카드도 들었을까요?", "말한 대로 될지… 한번 볼까요." },
        },
    };

    // 서버(LLM)가 만든 대사: (캐릭터, 키) → 대사들
    private static readonly Dictionary<string, List<string>> _server = new Dictionary<string, List<string>>();
    // 같은 대사가 연달아 나오지 않게 마지막으로 쓴 대사를 기억
    private static readonly Dictionary<string, string> _lastUsed = new Dictionary<string, string>();
    private static readonly Random _rng = new Random();

    public static int ServerLineCount { get; private set; }

    /// <summary>서버에서 받은 대사를 등록(같은 키에 여러 개 가능).</summary>
    public static void AddServerLine(string character, string key, string line)
    {
        if (string.IsNullOrWhiteSpace(character) || string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(line)) return;
        string k = character + "|" + key;
        if (!_server.TryGetValue(k, out var list)) _server[k] = list = new List<string>();
        if (!list.Contains(line)) { list.Add(line); ServerLineCount++; }
    }

    public static void ClearServerLines()
    {
        _server.Clear();
        ServerLineCount = 0;
    }

    /// <summary>
    /// 캐릭터의 한마디를 고른다. 서버 대사가 있으면 70% 확률로 서버 대사, 아니면 기본 대사.
    /// rival 키가 없으면 player 키로, 그래도 없으면 null.
    /// </summary>
    public static string Pick(string character, string key)
    {
        var candidates = new List<string>();
        if (_server.TryGetValue(character + "|" + key, out var fromServer) && fromServer.Count > 0 && _rng.NextDouble() < 0.7)
            candidates.AddRange(fromServer);
        if (candidates.Count == 0 && Defaults.TryGetValue(character, out var byKey))
        {
            if (byKey.TryGetValue(key, out var lines)) candidates.AddRange(lines);
            else if (key.EndsWith("_rival") && byKey.TryGetValue(key.Replace("_rival", "_player"), out var alt)) candidates.AddRange(alt);
        }
        if (candidates.Count == 0 && fromServer != null) candidates.AddRange(fromServer);
        if (candidates.Count == 0) return null;

        string lastKey = character + "|" + key;
        _lastUsed.TryGetValue(lastKey, out var last);
        if (candidates.Count > 1) candidates.Remove(last);
        string pick = candidates[_rng.Next(candidates.Count)];
        _lastUsed[lastKey] = pick;
        return pick;
    }

    /// <summary>
    /// 게임 시작 첫마디. 서버 묶음의 game_start 는 마이룸 대화(기억)를 반영해 만든 것이라 있으면 우선,
    /// 없으면 홈에서 최근에 대화했는지에 따라 기본 대사를 고른다.
    /// </summary>
    public static string PickGameStart(string character, bool talkedRecently)
    {
        if (_server.TryGetValue(character + "|game_start", out var fromServer) && fromServer.Count > 0)
            return fromServer[_rng.Next(fromServer.Count)];
        return Pick(character, talkedRecently ? "game_start_talked" : "game_start");
    }

    /// <summary>하이라이트 종류 → 대사 키의 앞부분. 대사가 없는 종류는 null.</summary>
    public static string KeyOf(HighlightType t)
    {
        switch (t)
        {
            case HighlightType.LastCard:    return "last_card";
            case HighlightType.Comeback:    return "comeback";
            case HighlightType.Blockade:    return "blockade";
            case HighlightType.PassStreak:  return "stuck";
            case HighlightType.CloseRace:   return "close_race";
            default:                        return null;
        }
    }
}
