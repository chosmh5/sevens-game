/// <summary>
/// 마을에서 상호작용 가능한 오브젝트 인터페이스
/// NPC, 상점, 오브젝트 등에 구현
/// </summary>
public interface IInteractable
{
    /// <summary>상호작용 가능 여부</summary>
    bool CanInteract();

    /// <summary>플레이어가 범위에 들어왔을 때 (UI 표시 등)</summary>
    void OnFocused();

    /// <summary>플레이어가 범위에서 나갔을 때</summary>
    void OnFocusLost();

    /// <summary>실제 상호작용 실행</summary>
    void Interact(VillagePlayerController player);
}
