using UnityEngine;

/// <summary>
/// 씬 전체를 관리합니다.
/// Hierarchy: RoomManager(이 스크립트) + ServerClient(같은 오브젝트에 추가)
/// </summary>
public class RoomManager : MonoBehaviour
{
    [Header("캐릭터 프리팹")]
    [SerializeField] private GameObject characterPrefab;

    [Header("스폰 지점 (비워두면 원점)")]
    [SerializeField] private Transform[] spawnPoints;

    void Start()
    {
        if (characterPrefab == null) return;

        int count = spawnPoints != null && spawnPoints.Length > 0
            ? spawnPoints.Length
            : 1;

        for (int i = 0; i < count; i++)
        {
            var pos = spawnPoints != null && i < spawnPoints.Length
                ? spawnPoints[i].position
                : Vector3.zero;

            Instantiate(characterPrefab, pos, Quaternion.identity);
        }
    }
}
