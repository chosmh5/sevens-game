using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>가로·세로 선분만으로 된 꺾은선(계단선). 선분마다 사각형 하나라 이음새가 깔끔하다.</summary>
public class StepLineGraphic : MaskableGraphic
{
    public float Thickness = 4f;
    private readonly List<Vector2> _points = new List<Vector2>();

    /// <summary>점들(이 RectTransform 의 왼쪽 아래 기준 좌표).</summary>
    public void SetPoints(List<Vector2> pts)
    {
        _points.Clear();
        _points.AddRange(pts);
        SetVerticesDirty();
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (_points.Count < 2) return;
        // 피벗 기준 로컬 좌표로 옮김
        Vector2 origin = -rectTransform.pivot * rectTransform.rect.size;
        float h = Thickness * 0.5f;
        Color32 c = color;
        for (int i = 1; i < _points.Count; i++)
        {
            Vector2 a = _points[i - 1] + origin, b = _points[i] + origin;
            Vector2 min = Vector2.Min(a, b) - new Vector2(h, h);
            Vector2 max = Vector2.Max(a, b) + new Vector2(h, h);
            int v = vh.currentVertCount;
            vh.AddVert(new Vector3(min.x, min.y), c, new Vector2(0f, 0f));
            vh.AddVert(new Vector3(min.x, max.y), c, new Vector2(0f, 1f));
            vh.AddVert(new Vector3(max.x, max.y), c, new Vector2(1f, 1f));
            vh.AddVert(new Vector3(max.x, min.y), c, new Vector2(1f, 0f));
            vh.AddTriangle(v, v + 1, v + 2);
            vh.AddTriangle(v + 2, v + 3, v);
        }
    }
}
