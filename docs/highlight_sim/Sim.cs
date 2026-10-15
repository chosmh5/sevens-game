// 하이라이트 감지기 시뮬레이션 검증 (Unity 없이 mono로 실행)
using System;
using System.Collections.Generic;
using System.Linq;

public static class Sim
{
    static int failures = 0;
    static void Check(bool ok, string msg) { Console.WriteLine((ok ? "  OK   " : "  FAIL ") + msg); if (!ok) failures++; }

    public static int Main(string[] args)
    {
        int games = args.Length > 0 ? int.Parse(args[0]) : 2000;
        var rng = new Random(7);
        var totals = new Dictionary<HighlightType, int>();
        foreach (HighlightType t in Enum.GetValues(typeof(HighlightType))) totals[t] = 0;
        int totalTurns = 0, bigMoments = 0, maxPerTurn = 0;
        int playerComebacks = 0, gamesWithAnyReaction = 0;
        string sampleLog = null;

        for (int g = 0; g < games; g++)
        {
            var lines = new Dictionary<Suit, FieldLine>();
            foreach (Suit s in Enum.GetValues(typeof(Suit))) { lines[s] = new FieldLine(s); lines[s].Place(new Card(s, 7)); }
            var deck = new List<Card>();
            foreach (Suit s in Enum.GetValues(typeof(Suit))) for (int n = 1; n <= 13; n++) if (n != 7) deck.Add(new Card(s, n));
            deck = deck.OrderBy(_ => rng.Next()).ToList();
            var hands = new List<Card>[4];
            for (int p = 0; p < 4; p++) hands[p] = deck.Skip(p * 12).Take(12).ToList();

            var det = new SevensHighlightDetector(4, 3);
            det.Reset(hands.Select(h => h.Count).ToArray());
            int cur = 0, turns = 0, reactions = 0;
            var log = new List<string>();
            while (true)
            {
                var played = new List<Card>();
                // 사람(0)은 추가 장을 30% 확률로 멈춤, AI는 Normal 난이도(20%)
                double overlook = cur == 0 ? 0.30 : 0.20;
                for (int k = 0; k < 3; k++)
                {
                    var playable = hands[cur].Where(c => lines[c.Suit].CanPlace(c)).ToList();
                    if (playable.Count == 0) break;
                    if (k > 0 && rng.NextDouble() < overlook) break;
                    var c = playable[rng.Next(playable.Count)];
                    lines[c.Suit].Place(c);
                    hands[cur].Remove(c);
                    played.Add(c);
                    if (hands[cur].Count == 0) break;
                }
                turns++;
                if (hands[cur].Count == 0) { log.Add($"턴 {turns}: 좌석 {cur} 승리"); break; }
                var counts = hands.Select(h => h.Count).ToArray();
                var ev = det.OnTurnEnded(cur, played, counts);
                if (ev.Count > maxPerTurn) maxPerTurn = ev.Count;
                foreach (var e in ev)
                {
                    totals[e.Type]++;
                    if (e.Intensity >= 3) bigMoments++;
                    if (e.Intensity >= 2) reactions++;
                    if (e.Type == HighlightType.Comeback && e.Actor == 0) playerComebacks++;
                    log.Add($"  {e}  손패=[{string.Join(",", counts)}]");
                }
                cur = (cur + 1) % 4;
                if (turns > 500) { Check(false, "무한 루프"); break; }
            }
            totalTurns += turns;
            if (reactions > 0) gamesWithAnyReaction++;
            if (sampleLog == null && det.History.Any(e => e.Type == HighlightType.Comeback)) sampleLog = string.Join("\n", log);
        }

        Console.WriteLine($"== {games}판 시뮬레이션 (평균 {totalTurns / (double)games:F1}턴)");
        foreach (var kv in totals) Console.WriteLine($"  {kv.Key,-13} 판당 {kv.Value / (double)games:F2}회");
        double perGameBig = bigMoments / (double)games;
        Console.WriteLine($"  큰 순간(강도3) 판당 {perGameBig:F2}회, 한 턴 최대 {maxPerTurn}건, 반응 있는 판 {100.0 * gamesWithAnyReaction / games:F0}%");
        Console.WriteLine($"  플레이어 역전 판당 {playerComebacks / (double)games:F2}회");
        Console.WriteLine("\n== 역전이 나온 예시 판\n" + sampleLog);

        Console.WriteLine("\n== 규칙 확인");
        Check(totals[HighlightType.LastCard] / (double)games > 0.5, "마지막 한 장은 거의 매 판 나온다");
        Check(perGameBig >= 0.8 && perGameBig <= 4.0, $"큰 순간이 판당 0.8~4회 (너무 잦지도 드물지도 않음): {perGameBig:F2}");
        Check(totals[HighlightType.Comeback] / (double)games <= 1.5, "역전은 판당 1.5회 이하");
        double reactPerGame = totals.Where(kv => kv.Key != HighlightType.Reach && kv.Key != HighlightType.LineComplete && kv.Key != HighlightType.PassStreak).Sum(kv => kv.Value) / (double)games;
        Check(reactPerGame >= 2 && reactPerGame <= 6, $"캐릭터 반응(강도 2 이상)이 판당 2~6회: {reactPerGame:F2} (평균 22턴 기준)");
        Check(100.0 * gamesWithAnyReaction / games >= 90, "90% 이상의 판에서 캐릭터 반응이 최소 1번");

        // 손으로 만든 시나리오: 역전
        var d = new SevensHighlightDetector(4, 3);
        d.Reset(new[] { 12, 12, 12, 12 });
        d.OnTurnEnded(1, Cards(3), new[] { 12, 9, 12, 12 });   // 1번 선두
        d.OnTurnEnded(2, Cards(1), new[] { 12, 9, 11, 12 });
        d.OnTurnEnded(3, Cards(1), new[] { 12, 9, 11, 11 });
        d.OnTurnEnded(0, Cards(3), new[] { 9, 9, 11, 11 });    // 0번 3장 뒤처졌다가 동률
        d.OnTurnEnded(1, Cards(0), new[] { 9, 9, 11, 11 });
        d.OnTurnEnded(2, Cards(0), new[] { 9, 9, 11, 11 });
        d.OnTurnEnded(3, Cards(0), new[] { 9, 9, 11, 11 });
        var e2 = d.OnTurnEnded(0, Cards(2), new[] { 7, 9, 11, 11 }); // 0번 단독 선두
        Check(e2.Any(x => x.Type == HighlightType.Comeback && x.Actor == 0 && x.Other == 1), "3장 뒤처졌던 플레이어가 선두 → 역전(상대=1번)");
        Check(e2.Any(x => x.Type == HighlightType.Comeback && x.Intensity == 3), "역전은 강도 3");
        Check(d.History.Any(x => x.Type == HighlightType.PassStreak) == false, "1번씩 패스는 '막힘' 아님");
        var e3 = d.OnTurnEnded(1, Cards(0), new[] { 7, 9, 11, 11 });
        Check(e3.Any(x => x.Type == HighlightType.PassStreak && x.Actor == 1 && x.Count == 2), "같은 사람 2연속 패스 → 막힘");
        var e4 = d.OnTurnEnded(2, Cards(3), new[] { 7, 9, 8, 11 });
        Check(!e4.Any(x => x.Type == HighlightType.TripleCombo), "3장을 냈어도 판세가 그대로면 하이라이트 아님");
        var e4b = d.OnTurnEnded(3, Cards(3), new[] { 7, 9, 8, 4 });
        Check(e4b.Any(x => x.Type == HighlightType.TripleCombo && x.Actor == 3), "3장으로 4장 이하가 되면 3장 연속");
        var e5 = d.OnTurnEnded(0, Cards(3), new[] { 4, 9, 8, 11 });
        e5 = d.OnTurnEnded(0, Cards(1), new[] { 3, 9, 8, 11 });
        Check(e5.Any(x => x.Type == HighlightType.Reach && x.Actor == 0), "처음 3장 이하 → 곧 끝남");
        var e6 = d.OnTurnEnded(0, Cards(2), new[] { 1, 9, 8, 11 });
        Check(e6.Any(x => x.Type == HighlightType.LastCard && x.Actor == 0 && x.Intensity == 3), "2장 이상 → 1장 = 마지막 한 장(강도 3)");
        var sum = d.Summary(i => i == 0 ? "플레이어" : new[] { "", "세비-호", "류", "진" }[i]);
        Console.WriteLine("  요약: " + string.Join(", ", sum));
        Check(sum.Count > 0 && sum[0].Contains("플레이어"), "요약에 큰 순간이 먼저 온다");

        Console.WriteLine(failures == 0 ? "\nALL PASSED" : $"\n{failures} FAILED");
        return failures == 0 ? 0 : 1;
    }

    static List<Card> Cards(int n) => Enumerable.Range(0, n).Select(i => new Card(Suit.Spade, 1)).ToList();
}
