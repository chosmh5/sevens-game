using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// 마을 그리드 시스템
/// - 건물/오브젝트 배치를 위한 그리드
/// - 에디터에서 시각적 그리드 표시
/// - 스냅 기능
/// </summary>
public class VillageGrid : MonoBehaviour
{
    public static VillageGrid Instance { get; private set; }

    [Header("Grid Settings")]
    [SerializeField] private int gridWidth = 30;
    [SerializeField] private int gridHeight = 30;
    [SerializeField] private float cellSize = 2f;

    [Header("Visual")]
    [SerializeField] private bool showGrid = true;
    [SerializeField] private Color gridColor = new Color(1, 1, 1, 0.15f);
    [SerializeField] private Color occupiedColor = new Color(1, 0, 0, 0.3f);

    // 그리드 점유 상태
    private bool[,] occupied;

    private void Awake()
    {
        Instance = this;
        occupied = new bool[gridWidth, gridHeight];
    }

    /// <summary>
    /// 월드 좌표를 그리드 좌표로 변환
    /// </summary>
    public Vector2Int WorldToGrid(Vector3 worldPos)
    {
        Vector3 local = worldPos - transform.position;
        int x = Mathf.RoundToInt(local.x / cellSize);
        int z = Mathf.RoundToInt(local.z / cellSize);
        return new Vector2Int(
            Mathf.Clamp(x, 0, gridWidth - 1),
            Mathf.Clamp(z, 0, gridHeight - 1)
        );
    }

    /// <summary>
    /// 그리드 좌표를 월드 좌표로 변환 (스냅)
    /// </summary>
    public Vector3 GridToWorld(Vector2Int gridPos)
    {
        return transform.position + new Vector3(
            gridPos.x * cellSize,
            0,
            gridPos.y * cellSize
        );
    }

    /// <summary>
    /// 월드 좌표를 그리드에 스냅
    /// </summary>
    public Vector3 SnapToGrid(Vector3 worldPos)
    {
        Vector2Int gridPos = WorldToGrid(worldPos);
        return GridToWorld(gridPos);
    }

    /// <summary>
    /// 해당 그리드 칸이 비어있는지 확인
    /// </summary>
    public bool IsCellFree(Vector2Int gridPos, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                int gx = gridPos.x + x;
                int gy = gridPos.y + y;

                if (gx < 0 || gx >= gridWidth || gy < 0 || gy >= gridHeight)
                    return false;
                if (occupied[gx, gy])
                    return false;
            }
        }
        return true;
    }

    /// <summary>
    /// 그리드 칸 점유
    /// </summary>
    public void OccupyCells(Vector2Int gridPos, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                int gx = gridPos.x + x;
                int gy = gridPos.y + y;
                if (gx >= 0 && gx < gridWidth && gy >= 0 && gy < gridHeight)
                    occupied[gx, gy] = true;
            }
        }
    }

    /// <summary>
    /// 그리드 칸 해제
    /// </summary>
    public void FreeCells(Vector2Int gridPos, Vector2Int size)
    {
        for (int x = 0; x < size.x; x++)
        {
            for (int y = 0; y < size.y; y++)
            {
                int gx = gridPos.x + x;
                int gy = gridPos.y + y;
                if (gx >= 0 && gx < gridWidth && gy >= 0 && gy < gridHeight)
                    occupied[gx, gy] = false;
            }
        }
    }

    private void OnDrawGizmos()
    {
        if (!showGrid) return;

        Vector3 origin = transform.position;

        for (int x = 0; x <= gridWidth; x++)
        {
            Vector3 start = origin + new Vector3(x * cellSize, 0.01f, 0);
            Vector3 end = origin + new Vector3(x * cellSize, 0.01f, gridHeight * cellSize);
            Gizmos.color = gridColor;
            Gizmos.DrawLine(start, end);
        }

        for (int z = 0; z <= gridHeight; z++)
        {
            Vector3 start = origin + new Vector3(0, 0.01f, z * cellSize);
            Vector3 end = origin + new Vector3(gridWidth * cellSize, 0.01f, z * cellSize);
            Gizmos.color = gridColor;
            Gizmos.DrawLine(start, end);
        }

        // 점유된 셀 표시
        if (occupied != null)
        {
            for (int x = 0; x < gridWidth; x++)
            {
                for (int z = 0; z < gridHeight; z++)
                {
                    if (occupied[x, z])
                    {
                        Vector3 center = origin + new Vector3(
                            x * cellSize + cellSize / 2,
                            0.02f,
                            z * cellSize + cellSize / 2
                        );
                        Gizmos.color = occupiedColor;
                        Gizmos.DrawCube(center, new Vector3(cellSize, 0.01f, cellSize));
                    }
                }
            }
        }
    }
}
