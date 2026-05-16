using UnityEngine;

/// <summary>
/// 플레이어가 맵 밖으로 떨어졌을 때 부드럽게 인도 위로 데려다 놓는 컨트롤러.
///
/// 흐름:
///   1) Y가 fallThresholdY 아래로 떨어지면 발동 (XZ 이탈만으로는 발동 안 함 → 잠깐 자유낙하 허용)
///   2) Carry 페이즈: 현재 위치 → 안전 지점 위 2m 까지 포물선 궤적으로 부드럽게 이동
///      (CharacterController 비활성, 입력 잠금)
///   3) Carry 완료 후 CC 다시 활성화 + 짧은 잠금 유지 → 중력으로 자연 착지
///   4) 착지하면 입력 잠금 해제
/// </summary>
[DisallowMultipleComponent]
public class VegasPlayerRespawner : MonoBehaviour
{
    [Header("Fall Detection")]
    [Tooltip("Y가 이 값 아래로 떨어지면 캐리 시작 (잠깐 떨어진 뒤 발동되도록 음수)")]
    public float fallThresholdY = -8f;

    [Header("Respawn Target")]
    [Tooltip("인도 위 캐리 종료 높이 (m) — 끝나고 이만큼에서 자유낙하 시작")]
    public float airDropHeight = 2f;

    [Header("Carry Motion")]
    [Tooltip("포물선 궤적의 추가 피크 높이 (m) — 빌딩 위로 넘어가기 위한 여유 높이")]
    public float arcPeakHeight = 12f;

    [Tooltip("캐리 평균 속도 (m/s) — 거리에 따라 duration 결정")]
    public float carrySpeed = 7f;

    [Tooltip("캐리 최소/최대 지속 시간 (초)")]
    public float carryDurationMin = 2.5f;
    public float carryDurationMax = 5.0f;

    [Header("Post-Carry")]
    [Tooltip("캐리 완료 후 착지 전 안전 잠금 시간 (초) — isGrounded 즉시 true 방지")]
    public float minPostCarryLock = 0.2f;

    private CharacterController cc;
    private Vector3 lastGroundedPos;
    private bool hasGroundedSample;

    // 상태
    private enum State { None, Carrying }
    private State state = State.None;

    // 캐리 데이터
    private Vector3 carryFrom;
    private Vector3 carryTo;
    private float carryStartTime;
    private float carryDuration;

    // 착지 전까지 입력 잠금
    private bool inputLocked;
    private float lockMinUntil;

    private void Awake()
    {
        cc = GetComponent<CharacterController>();
        lastGroundedPos = transform.position;
    }

    private void Update()
    {
        if (state == State.Carrying)
        {
            TickCarry();
            return; // 캐리 중에는 다른 로직 정지
        }

        SampleGroundedPosition();
        UpdateInputLock();
        CheckFallTrigger();
    }

    // ──────────────────────────────────────────
    // Idle 상태 로직
    // ──────────────────────────────────────────

    private void SampleGroundedPosition()
    {
        if (cc == null || !cc.isGrounded) return;
        Vector3 p = transform.position;
        if (IsWithinCity(p))
        {
            lastGroundedPos = p;
            hasGroundedSample = true;
        }
    }

    /// <summary>
    /// 캐리 종료 후 착지 전까지 input 잠금 유지.
    /// 최소 시간 + isGrounded 둘 다 충족하면 해제.
    /// </summary>
    private void UpdateInputLock()
    {
        if (!inputLocked) return;
        if (Time.time < lockMinUntil) return;
        if (cc != null && cc.isGrounded) inputLocked = false;
    }

    private void CheckFallTrigger()
    {
        // XZ 이탈만으로는 발동하지 않는다 — Y가 충분히 내려가야 발동.
        // 즉 사용자가 절벽 끝에서 뛰어내리면 잠깐 자유낙하 시간을 누린 뒤 캐리 시작.
        if (transform.position.y < fallThresholdY)
        {
            BeginCarry();
        }
    }

    // ──────────────────────────────────────────
    // 캐리 시작 / 진행
    // ──────────────────────────────────────────

    private void BeginCarry()
    {
        Vector3 ground = ComputeRespawnGroundPosition();
        Vector3 from = transform.position;
        Vector3 to = ground + new Vector3(0, airDropHeight, 0);

        carryFrom = from;
        carryTo = to;

        // 거리 기반 시간 — 너무 짧지도 길지도 않게 클램프
        float dist = Vector3.Distance(from, to);
        carryDuration = Mathf.Clamp(dist / Mathf.Max(0.1f, carrySpeed),
                                    carryDurationMin, carryDurationMax);
        carryStartTime = Time.time;

        // 캐리 동안에는 컨트롤러 비활성 → transform 직접 제어 가능 + 중력/콜리전 영향 없음
        if (cc != null) cc.enabled = false;

        state = State.Carrying;
        inputLocked = true;

        Debug.Log($"[VegasRespawn] 캐리 시작: {from} → {to} ({carryDuration:F2}s, {dist:F1}m)");
    }

    private void TickCarry()
    {
        float t = (Time.time - carryStartTime) / carryDuration;
        if (t >= 1f)
        {
            EndCarry();
            return;
        }

        // ease in/out — 시작/끝 부드럽게
        float et = Mathf.SmoothStep(0f, 1f, t);

        // 포물선: XZ는 선형 보간, Y는 선형 + 4*peak*t*(1-t) 로 위로 솟아오름
        float x = Mathf.Lerp(carryFrom.x, carryTo.x, et);
        float z = Mathf.Lerp(carryFrom.z, carryTo.z, et);
        float yLine = Mathf.Lerp(carryFrom.y, carryTo.y, et);
        float arc = arcPeakHeight * 4f * et * (1f - et);
        float y = yLine + arc;

        transform.position = new Vector3(x, y, z);
    }

    private void EndCarry()
    {
        // 정확한 종료 위치(공중 2m)로 스냅
        transform.position = carryTo;
        lastGroundedPos = new Vector3(carryTo.x, carryTo.y - airDropHeight, carryTo.z);
        hasGroundedSample = true;

        // 컨트롤러 다시 활성 → 자유낙하로 자연 착지
        if (cc != null) cc.enabled = true;

        // 착지할 때까지 입력 잠금 유지
        inputLocked = true;
        lockMinUntil = Time.time + minPostCarryLock;

        state = State.None;
        Debug.Log($"[VegasRespawn] 캐리 종료 → 자연 낙하 착지 대기 ({carryTo})");
    }

    // ──────────────────────────────────────────
    // 안전 지점 계산
    // ──────────────────────────────────────────

    private bool IsWithinCity(Vector3 pos)
    {
        var gen = VegasCityGenerator.Instance;
        if (gen == null) return true;
        float w = gen.GridWidth  * gen.CellSize;
        float d = gen.GridHeight * gen.CellSize;
        Vector3 origin = gen.transform.position;
        return pos.x >= origin.x && pos.x <= origin.x + w
            && pos.z >= origin.z && pos.z <= origin.z + d;
    }

    private Vector3 ComputeRespawnGroundPosition()
    {
        // 1) 마지막 grounded 위치 우선
        if (hasGroundedSample && IsWithinCity(lastGroundedPos))
        {
            return SnapToGround(lastGroundedPos);
        }

        // 2) 가장 가까운 Sidewalk 셀
        var gen = VegasCityGenerator.Instance;
        if (gen != null && gen.CellTypes != null)
        {
            Vector3 p = transform.position;
            Vector2Int approx = gen.WorldToGrid(p);

            int bestX = -1, bestZ = -1, bestDist = int.MaxValue;
            for (int x = 0; x < gen.GridWidth; x++)
            {
                for (int z = 0; z < gen.GridHeight; z++)
                {
                    if (gen.CellTypes[x, z] != VegasCellType.Sidewalk) continue;
                    int dx = x - approx.x;
                    int dz = z - approx.y;
                    int dist = dx * dx + dz * dz;
                    if (dist < bestDist) { bestDist = dist; bestX = x; bestZ = z; }
                }
            }

            if (bestX >= 0)
            {
                Vector3 cellCenter = gen.GridToWorld(new Vector2Int(bestX, bestZ))
                                   + new Vector3(gen.CellSize / 2f, 0, gen.CellSize / 2f);
                return SnapToGround(cellCenter);
            }
        }

        // 3) 폴백 — 원점 위쪽에서 raycast
        return SnapToGround(new Vector3(0, 10, 0));
    }

    /// <summary>
    /// XZ 위치에서 아래로 raycast 해서 정확한 지면 Y로 보정
    /// </summary>
    private Vector3 SnapToGround(Vector3 pos)
    {
        Vector3 from = new Vector3(pos.x, pos.y + 50f, pos.z);
        if (Physics.Raycast(from, Vector3.down, out var hit, 200f))
        {
            return new Vector3(pos.x, hit.point.y + 0.02f, pos.z);
        }
        return new Vector3(pos.x, 0.2f, pos.z);
    }

    // ──────────────────────────────────────────
    // 외부 API
    // ──────────────────────────────────────────

    /// <summary>
    /// 외부 트리거용 강제 리스폰 (현재 위치에서 캐리 시작)
    /// </summary>
    public void ForceRespawn()
    {
        if (state == State.Carrying) return;
        BeginCarry();
    }

    /// <summary>
    /// 컨트롤러에서 폴링 — 캐리 중 OR 캐리 후 착지 전까지 true
    /// </summary>
    public bool IsInputLocked() => state == State.Carrying || inputLocked;
}
