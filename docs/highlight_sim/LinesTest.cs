using System;
using System.Linq;
public static class LinesTest
{
    public static int Main()
    {
        int fail = 0;
        void Check(bool ok, string m) { Console.WriteLine((ok ? "  OK   " : "  FAIL ") + m); if (!ok) fail++; }
        string[] chars = { "세비-호", "류", "진" };
        foreach (HighlightType t in Enum.GetValues(typeof(HighlightType)))
        {
            string k = CharacterReactionLines.KeyOf(t);
            if (k == null) continue;
            string[] persp = t == HighlightType.CloseRace ? new[] { "" }
                           : t == HighlightType.Blockade ? new[] { "_player" }      // 봉쇄는 플레이어가 막은 경우만
                           : new[] { "_self", "_player", "_rival" };
            foreach (var c in chars) foreach (var p in persp)
            {
                var line = CharacterReactionLines.Pick(c, k + p);
                if (line == null) Check(false, $"{c} {k + p} 대사 없음");
                else if (!(line.TrimEnd('…', '.', '?', '!', ' ').EndsWith("요") || line.TrimEnd('…', '.', '?', '!', ' ').EndsWith("죠")))
                    Check(false, $"{c} {k + p} 존댓말 아님: {line}");
            }
        }
        Check(fail == 0, "모든 상황·관점·캐릭터에 존댓말 기본 대사가 있음");
        // 같은 대사 연속 방지
        var a = CharacterReactionLines.Pick("류", "comeback_player");
        var b = CharacterReactionLines.Pick("류", "comeback_player");
        Check(a != b, $"같은 대사가 연달아 나오지 않음 ({a} / {b})");
        // 서버 대사 우선(70%)
        CharacterReactionLines.AddServerLine("진", "blockade_player", "서버에서 온 대사예요…");
        int hits = Enumerable.Range(0, 200).Count(_ => CharacterReactionLines.Pick("진", "blockade_player") == "서버에서 온 대사예요…");
        Check(hits > 80 && hits < 180, $"서버 대사가 섞여 나옴 (200번 중 {hits}번)");
        Check(CharacterReactionLines.Pick("모르는캐릭터", "comeback_self") == null, "모르는 캐릭터는 null");
        Console.WriteLine(fail == 0 ? "ALL PASSED" : $"{fail} FAILED");
        return fail;
    }
}
