using UnityEngine;
using System;
using System.Collections.Generic;

/// <summary>
/// 라스베가스 맵 데이터 — 직렬화 가능한 맵 레이아웃
/// - 건물 배치, 도로 레이아웃을 JSON으로 저장/로드
/// - 그리드 기반 셀 타입 관리
/// </summary>
[Serializable]
public class VegasMapData
{
    public int gridWidth = 40;
    public int gridHeight = 40;
    public float cellSize = 2f;
    public List<BuildingPlacement> buildings = new List<BuildingPlacement>();
    public string mapName = "My Vegas";

    [Serializable]
    public class BuildingPlacement
    {
        public int gridX;
        public int gridZ;
        public int sizeX;
        public int sizeZ;
        public VegasBuildingType buildingType;
        public int rotation; // 0, 90, 180, 270
        public int floors;
        public int colorIndex;
        public string customName;
    }
}

/// <summary>
/// 셀 타입 — 그리드 각 칸의 용도
/// </summary>
public enum VegasCellType
{
    Empty,
    Road,
    Sidewalk,
    Building,
    Decoration,
    Crosswalk
}

/// <summary>
/// 건물 종류 — 라스베가스 테마
/// </summary>
public enum VegasBuildingType
{
    Casino,         // 카지노 — 대형, 화려한 네온
    Hotel,          // 호텔 — 고층, 유리 느낌
    Shop,           // 상점 — 소형, 간판
    Bar,            // 바/클럽 — 중형, 네온 강조
    Restaurant,     // 레스토랑 — 중형
    Billboard,      // 빌보드/간판 — 장식용
    Fountain,       // 분수 — 장식용
    ParkingLot      // 주차장 — 빈 공간 채움
}

/// <summary>
/// 건물 타입별 설정
/// </summary>
public static class VegasBuildingConfig
{
    public struct BuildingSpec
    {
        public Vector2Int minSize;
        public Vector2Int maxSize;
        public int minFloors;
        public int maxFloors;
        public float neonIntensity;
        public string displayName;
    }

    public static BuildingSpec GetSpec(VegasBuildingType type)
    {
        return type switch
        {
            VegasBuildingType.Casino => new BuildingSpec
            {
                minSize = new Vector2Int(4, 3),
                maxSize = new Vector2Int(6, 5),
                minFloors = 2,
                maxFloors = 5,
                neonIntensity = 2f,
                displayName = "카지노"
            },
            VegasBuildingType.Hotel => new BuildingSpec
            {
                minSize = new Vector2Int(3, 3),
                maxSize = new Vector2Int(4, 4),
                minFloors = 5,
                maxFloors = 12,
                neonIntensity = 1.2f,
                displayName = "호텔"
            },
            VegasBuildingType.Shop => new BuildingSpec
            {
                minSize = new Vector2Int(2, 2),
                maxSize = new Vector2Int(3, 2),
                minFloors = 1,
                maxFloors = 2,
                neonIntensity = 1.5f,
                displayName = "상점"
            },
            VegasBuildingType.Bar => new BuildingSpec
            {
                minSize = new Vector2Int(2, 2),
                maxSize = new Vector2Int(4, 3),
                minFloors = 1,
                maxFloors = 3,
                neonIntensity = 2.5f,
                displayName = "바/클럽"
            },
            VegasBuildingType.Restaurant => new BuildingSpec
            {
                minSize = new Vector2Int(2, 2),
                maxSize = new Vector2Int(3, 3),
                minFloors = 1,
                maxFloors = 2,
                neonIntensity = 1f,
                displayName = "레스토랑"
            },
            VegasBuildingType.Billboard => new BuildingSpec
            {
                minSize = new Vector2Int(1, 1),
                maxSize = new Vector2Int(2, 1),
                minFloors = 1,
                maxFloors = 1,
                neonIntensity = 3f,
                displayName = "빌보드"
            },
            VegasBuildingType.Fountain => new BuildingSpec
            {
                minSize = new Vector2Int(2, 2),
                maxSize = new Vector2Int(3, 3),
                minFloors = 1,
                maxFloors = 1,
                neonIntensity = 1f,
                displayName = "분수"
            },
            VegasBuildingType.ParkingLot => new BuildingSpec
            {
                minSize = new Vector2Int(3, 3),
                maxSize = new Vector2Int(5, 4),
                minFloors = 1,
                maxFloors = 1,
                neonIntensity = 0.3f,
                displayName = "주차장"
            },
            _ => new BuildingSpec
            {
                minSize = new Vector2Int(2, 2),
                maxSize = new Vector2Int(3, 3),
                minFloors = 1,
                maxFloors = 3,
                neonIntensity = 1f,
                displayName = "건물"
            }
        };
    }

    /// <summary>
    /// 건물 타입별 대표 색상 팔레트
    /// </summary>
    public static Color[] GetNeonColors(VegasBuildingType type, int colorIndex = 0)
    {
        Color[][] palettes = type switch
        {
            VegasBuildingType.Casino => new[]
            {
                new[] { new Color(1f, 0.84f, 0f), new Color(1f, 0.2f, 0.2f), Color.white },      // 골드 + 레드
                new[] { new Color(0.5f, 0f, 1f), new Color(1f, 0f, 0.8f), Color.white },           // 퍼플 + 핑크
                new[] { new Color(0f, 1f, 0.5f), new Color(0f, 0.8f, 1f), Color.white },           // 그린 + 시안
            },
            VegasBuildingType.Hotel => new[]
            {
                new[] { new Color(0.3f, 0.7f, 1f), new Color(0.8f, 0.9f, 1f), Color.white },      // 블루 + 화이트
                new[] { new Color(1f, 0.6f, 0.8f), new Color(0.9f, 0.8f, 1f), Color.white },       // 핑크 + 라벤더
            },
            VegasBuildingType.Bar => new[]
            {
                new[] { new Color(1f, 0f, 0.4f), new Color(1f, 0f, 0.8f), new Color(0.8f, 0f, 1f) }, // 핫핑크
                new[] { new Color(0f, 1f, 0.4f), new Color(1f, 1f, 0f), Color.white },                 // 그린 + 옐로우
            },
            _ => new[]
            {
                new[] { new Color(0.4f, 1f, 0.95f), new Color(0.3f, 0.7f, 1f), Color.white },     // 시안 + 블루
                new[] { new Color(1f, 0.5f, 0f), new Color(1f, 0.8f, 0f), Color.white },           // 오렌지 + 골드
            }
        };

        int idx = Mathf.Abs(colorIndex) % palettes.Length;
        return palettes[idx];
    }
}
