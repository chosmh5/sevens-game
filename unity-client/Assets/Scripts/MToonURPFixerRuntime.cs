using UnityEngine;

/// <summary>
/// 씬에 이미 배치된 VRM 캐릭터(seven.prefab 등)에 붙여 두면 Start 시점에
/// 자식 계층의 MToon 머티리얼을 URP/Lit 으로 자동 변환한다.
///
/// 사용: 캐릭터 루트 GameObject 에 이 컴포넌트를 추가만 하면 된다.
///       VRMLoader 를 통해 동적으로 로드되는 캐릭터는 VRMLoader 가 이미 변환을
///       호출하므로 이 컴포넌트는 불필요하다.
/// </summary>
[DisallowMultipleComponent]
public class MToonURPFixerRuntime : MonoBehaviour
{
    [Tooltip("씬 시작 시 자동으로 변환할지 여부")]
    [SerializeField] private bool runOnStart = true;

    [Tooltip("Awake/Start 어느 쪽에서 실행할지. 다른 스크립트가 머티리얼을 만지기 전이면 Awake 권장.")]
    [SerializeField] private bool runInAwake = false;

    void Awake()
    {
        if (runOnStart && runInAwake) MToonURPFixer.FixHierarchy(gameObject);
    }

    void Start()
    {
        if (runOnStart && !runInAwake) MToonURPFixer.FixHierarchy(gameObject);
    }

    [ContextMenu("Run Fix Now")]
    public void RunFixNow() => MToonURPFixer.FixHierarchy(gameObject);
}
