using UnityEngine;

/// <summary>
/// 마을 건물 컴포넌트
/// - 그리드 스냅 배치
/// - 크기 정의
/// - 에디터에서 자동 스냅
/// </summary>
[ExecuteInEditMode]
public class VillageBuilding : MonoBehaviour
{
    [Header("Building Info")]
    [SerializeField] private string buildingName = "건물";
    [SerializeField] private BuildingType buildingType = BuildingType.House;

    [Header("Grid Size")]
    [SerializeField] private Vector2Int gridSize = new Vector2Int(2, 2);

    [Header("Snap")]
    [SerializeField] private bool snapToGrid = true;

    public enum BuildingType
    {
        House,
        Shop,
        PublicBuilding,
        Decoration
    }

    public string BuildingName => buildingName;
    public BuildingType Type => buildingType;
    public Vector2Int GridSize => gridSize;

    private void Update()
    {
        #if UNITY_EDITOR
        // 에디터에서 자동 그리드 스냅
        if (!Application.isPlaying && snapToGrid)
        {
            VillageGrid grid = FindAnyObjectByType<VillageGrid>();
            if (grid != null)
            {
                Vector3 snapped = grid.SnapToGrid(transform.position);
                if (Vector3.Distance(transform.position, snapped) > 0.01f)
                {
                    transform.position = snapped;
                }
            }
        }
        #endif
    }

    private void OnDrawGizmos()
    {
        // 건물 차지 영역 표시
        float cellSize = 2f; // VillageGrid의 기본 cellSize와 맞춤
        VillageGrid grid = FindAnyObjectByType<VillageGrid>();
        if (grid != null)
        {
            // 그리드에서 cellSize를 가져올 수 없으므로 기본값 사용
        }

        Vector3 center = transform.position + new Vector3(
            gridSize.x * cellSize / 2f,
            0.5f,
            gridSize.y * cellSize / 2f
        );

        Color typeColor = buildingType switch
        {
            BuildingType.House => new Color(0.2f, 0.6f, 1f, 0.3f),
            BuildingType.Shop => new Color(1f, 0.8f, 0.2f, 0.3f),
            BuildingType.PublicBuilding => new Color(0.2f, 1f, 0.4f, 0.3f),
            BuildingType.Decoration => new Color(1f, 0.5f, 1f, 0.3f),
            _ => new Color(1, 1, 1, 0.3f)
        };

        Gizmos.color = typeColor;
        Gizmos.DrawCube(center, new Vector3(gridSize.x * cellSize, 1f, gridSize.y * cellSize));

        Gizmos.color = new Color(typeColor.r, typeColor.g, typeColor.b, 0.8f);
        Gizmos.DrawWireCube(center, new Vector3(gridSize.x * cellSize, 1f, gridSize.y * cellSize));
    }
}
