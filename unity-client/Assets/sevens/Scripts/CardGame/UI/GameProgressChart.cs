using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 결과 화면의 '판 흐름' 꺾은선 그래프. 가로 = 턴, 세로 = 남은 손패 수, 좌석마다 한 줄(좌석 색).
///
/// - 손패 수는 자기 턴에만 바뀌므로 계단식(가로→세로)으로 그린다. 레트로 픽셀 느낌과도 맞다.
/// - 세로축은 가중: 승부가 갈리는 0~6장을 크게, 별일 없는 7~12장은 위쪽에 눌러 그린다(그 구간은 바탕을 살짝 어둡게).
/// - 봉쇄·역전은 해당 줄 위에 작은 점 + 글자, 승자가 0장이 된 지점엔 금색 점.
/// - 열리면 왼쪽부터 선이 그려지는 짧은 연출(RectMask2D 폭을 늘림).
/// - 데이터는 SevensHighlightDetector.CountHistory / History. UI는 전부 코드로 만든다.
/// </summary>
public class GameProgressChart : MonoBehaviour
{
    private const float PadLeft = 44f, PadRight = 18f, PadTop = 46f, PadBottom = 34f;
    private const float RevealSeconds = 0.9f;
    // 세로축 가중: 초반(12~7장)은 별일이 없으니 0~Knee 장이 높이의 LowShare 를 차지하게 늘리고 위쪽은 눌러 그린다.
    private const int   Knee = 6;
    private const float LowShare = 0.72f;

    private RectTransform _reveal;
    private float _fullWidth;
    private float _t;

    /// <summary>
    /// parent 안을 꽉 채워 그래프를 만든다. counts[k] = k번째 턴이 끝난 뒤 좌석별 손패 수(0번 = 시작).
    /// winnerSeat 은 0장이 된 사람(금색 점). nameOf 는 좌석 → 이름(좌석 색 결정).
    /// </summary>
    public static GameProgressChart Build(RectTransform parent, IReadOnlyList<int[]> counts,
                                          IReadOnlyList<HighlightEvent> events, int winnerSeat, Func<int, string> nameOf)
    {
        for (int i = parent.childCount - 1; i >= 0; i--) Destroy(parent.GetChild(i).gameObject);
        if (counts == null || counts.Count < 2) return null;

        var root = RetroTheme.Stretch(RetroTheme.Node("ProgressChart", parent));
        RetroTheme.InsetBox(root, RetroTheme.Inset);
        var chart = root.gameObject.AddComponent<GameProgressChart>();

        Vector2 size = parent.rect.size;
        if (size.x <= 1f) size = parent.sizeDelta;          // 아직 레이아웃 전이면 지정 크기
        float plotW = size.x - PadLeft - PadRight;
        float plotH = size.y - PadTop - PadBottom;

        var title = RetroTheme.Label(root, "Title", "판 흐름", 22f, RetroTheme.Text, true, TextAlignmentOptions.Left);
        RetroTheme.Place((RectTransform)title.transform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -8f), new Vector2(200f, 30f));
        var unit = RetroTheme.Label(root, "Unit", "남은 카드", 16f, RetroTheme.Muted, false, TextAlignmentOptions.Right);
        RetroTheme.Place((RectTransform)unit.transform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -12f), new Vector2(200f, 24f));

        var plot = RetroTheme.Place(RetroTheme.Node("Plot", root), Vector2.zero, Vector2.zero,
                                    new Vector2(PadLeft, PadBottom), new Vector2(plotW, plotH));

        int seats = counts[0].Length;
        int turns = counts.Count - 1;
        int maxY = 1;
        foreach (var c in counts) foreach (var v in c) maxY = Mathf.Max(maxY, v);
        float X(int turn) => plotW * turn / Mathf.Max(1, turns);
        bool weighted = maxY > Knee + 1;
        float Y(int count) => !weighted ? plotH * count / maxY
            : count <= Knee ? plotH * LowShare * count / Knee
            : plotH * (LowShare + (1f - LowShare) * (count - Knee) / (maxY - Knee));

        // 눌러 그린 구간(Knee 장 초과) 바탕
        if (weighted)
        {
            var band = RetroTheme.Place(RetroTheme.Node("CompressedBand", plot), Vector2.zero, Vector2.zero,
                                        new Vector2(0f, Y(Knee)), new Vector2(plotW, plotH - Y(Knee)));
            var bi = band.gameObject.AddComponent<Image>();
            bi.color = RetroTheme.WithAlpha(RetroTheme.Ink, 0.45f);
            bi.raycastTarget = false;
        }

        // 눈금: 가로줄 + 왼쪽 숫자 / 아래 턴 번호
        var yTicks = new List<int>();
        if (weighted) { for (int v = 0; v <= Knee; v += 2) yTicks.Add(v); yTicks.Add(maxY); }
        else for (int v = 0; v <= maxY; v += maxY > 8 ? 4 : 2) yTicks.Add(v);
        foreach (int v in yTicks)
        {
            var g = RetroTheme.Place(RetroTheme.Node($"Grid{v}", plot), Vector2.zero, new Vector2(0f, 0.5f),
                                     new Vector2(0f, Y(v)), new Vector2(plotW, v == 0 ? 2f : 1f));
            var gi = g.gameObject.AddComponent<Image>();
            gi.color = RetroTheme.WithAlpha(RetroTheme.Faint, v == 0 ? 0.8f : 0.35f);
            gi.raycastTarget = false;
            var lab = RetroTheme.Label(plot, $"Y{v}", v.ToString(), 16f, RetroTheme.Muted, false, TextAlignmentOptions.Right);
            RetroTheme.Place((RectTransform)lab.transform, Vector2.zero, new Vector2(1f, 0.5f), new Vector2(-8f, Y(v)), new Vector2(40f, 20f));
        }
        int xStep = turns <= 20 ? 5 : turns <= 40 ? 10 : 20;
        for (int t = 0; t <= turns; t += xStep)
            XLabel(plot, X(t), t == 0 ? "시작" : t.ToString());
        if (turns % xStep >= xStep / 2) XLabel(plot, X(turns), $"{turns}턴");

        // 선(마스크 안) — 왼쪽부터 드러나는 연출
        var reveal = RetroTheme.Place(RetroTheme.Node("Reveal", plot), Vector2.zero, Vector2.zero,
                                      new Vector2(-10f, -10f), new Vector2(0f, plotH + 60f));
        reveal.gameObject.AddComponent<RectMask2D>();
        var inner = RetroTheme.Place(RetroTheme.Node("Inner", reveal), Vector2.zero, Vector2.zero,
                                     new Vector2(10f, 10f), new Vector2(plotW, plotH));

        // 겹치는 줄이 보이게 좌석마다 살짝 위아래로 비켜 그린다. 플레이어(0번)는 맨 위에 굵게.
        float Off(int seat) => (seat - (seats - 1) * 0.5f) * 2.5f;
        var order = new List<int>();
        for (int s = 1; s < seats; s++) order.Add(s);
        order.Add(0);
        foreach (int s in order)
        {
            var pts = new List<Vector2> { new Vector2(X(0), Y(counts[0][s]) + Off(s)) };
            for (int t = 1; t <= turns; t++)
            {
                if (counts[t][s] == counts[t - 1][s]) continue;
                pts.Add(new Vector2(X(t), Y(counts[t - 1][s]) + Off(s)));   // 가로
                pts.Add(new Vector2(X(t), Y(counts[t][s]) + Off(s)));       // 세로
            }
            pts.Add(new Vector2(X(turns), Y(counts[turns][s]) + Off(s)));

            var lineRT = RetroTheme.Place(RetroTheme.Node($"Line{s}", inner), Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(plotW, plotH));
            lineRT.gameObject.AddComponent<CanvasRenderer>();
            var line = lineRT.gameObject.AddComponent<StepLineGraphic>();
            line.raycastTarget = false;
            line.color = RetroTheme.SeatColor(nameOf(s));
            line.Thickness = s == 0 ? 5f : 3.5f;
            line.SetPoints(pts);
            var sh = lineRT.gameObject.AddComponent<Shadow>();
            sh.effectColor = RetroTheme.Ink;
            sh.effectDistance = new Vector2(0f, -2f);
        }

        // 표시: 봉쇄(막힌 사람 줄) · 역전(역전한 사람 줄) · 승자 0장
        if (events != null)
            foreach (var e in events)
            {
                if (e.Turn < 0 || e.Turn > turns) continue;
                int seat; string text;
                if (e.Type == HighlightType.Blockade && e.Other >= 0) { seat = e.Other; text = "봉쇄"; }
                else if (e.Type == HighlightType.Comeback && e.Actor >= 0) { seat = e.Actor; text = "역전"; }
                else continue;
                Marker(inner, new Vector2(X(e.Turn), Y(counts[e.Turn][seat]) + Off(seat)),
                       RetroTheme.SeatColor(nameOf(seat)), text);
            }
        if (winnerSeat >= 0 && winnerSeat < seats)
            Marker(inner, new Vector2(X(turns), Y(0) + Off(winnerSeat)), RetroTheme.Gold, null);

        chart._reveal = reveal;
        chart._fullWidth = plotW + 20f;
        return chart;
    }

    private void Update()
    {
        if (_reveal == null || _t >= RevealSeconds) return;
        _t += Time.unscaledDeltaTime;
        float k = Mathf.Clamp01(_t / RevealSeconds);
        float ease = 1f - Mathf.Pow(1f - k, 2f);
        _reveal.sizeDelta = new Vector2(_fullWidth * ease, _reveal.sizeDelta.y);
    }

    private static void XLabel(RectTransform plot, float x, string text)
    {
        var lab = RetroTheme.Label(plot, "X" + text, text, 16f, RetroTheme.Muted, false);
        RetroTheme.Place((RectTransform)lab.transform, Vector2.zero, new Vector2(0.5f, 1f), new Vector2(x, -6f), new Vector2(60f, 20f));
    }

    private static void Marker(RectTransform parent, Vector2 pos, Color color, string text)
    {
        var dot = RetroTheme.Place(RetroTheme.Node("Mark", parent), Vector2.zero, new Vector2(0.5f, 0.5f), pos, new Vector2(14f, 14f));
        var img = dot.gameObject.AddComponent<Image>();
        img.sprite = RetroTheme.PanelSprite(3, 2);
        img.type = Image.Type.Sliced;
        img.color = color;
        img.raycastTarget = false;
        if (string.IsNullOrEmpty(text)) return;
        var lab = RetroTheme.Label(parent, "MarkText", text, 16f, color, true);
        RetroTheme.Place((RectTransform)lab.transform, Vector2.zero, new Vector2(0.5f, 0f), pos + new Vector2(0f, 10f), new Vector2(60f, 20f));
        RetroTheme.AddTextShadow(lab, -1.2f);
    }
}
