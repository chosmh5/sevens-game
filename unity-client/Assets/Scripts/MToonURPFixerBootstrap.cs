using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 모든 씬 시작 시점에 자동으로 호출되어, 씬 내 모든 루트 GameObject 에 대해
/// MToonURPFixer.FixHierarchy() 를 실행한다.
///
/// 이렇게 해 두면:
///   1) VRMLoader 로 동적 로드되는 캐릭터 → VRMLoader 가 직접 호출
///   2) seven.prefab 처럼 씬에 정적 배치된 캐릭터 → 이 부트스트랩이 자동 처리
///
/// 결과적으로 사용자가 별도로 컴포넌트를 붙이거나 메뉴를 실행할 필요 없이 자동으로
/// 모든 MToon 머티리얼이 URP 호환으로 교체된다.
/// </summary>
internal static class MToonURPFixerBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        FixActiveScene();
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FixScene(scene);
    }

    private static void FixActiveScene() => FixScene(SceneManager.GetActiveScene());

    // 마이룸에서 Transparent 를 유지할 머티리얼 키워드 (나머지 body 는 모두 Cutoff)
    private static readonly string[] MyRoomTransparentKeywords = { "Tops" };

    private static void FixScene(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded) return;

        int total = 0;
        var roots = scene.GetRootGameObjects();

        foreach (var root in roots)
            total += MToonURPFixer.FixHierarchy(root);

        if (total > 0)
            Debug.Log($"[MToonURPFixerBootstrap] Scene '{scene.name}': {total} 개 머티리얼 변환");

        // 마이룸 씬: body 머티리얼 → Cutoff, Tops 머티리얼 → Transparent
        if (scene.name == "MyRoom")
        {
            int overrideTotal = 0;
            foreach (var root in roots)
                overrideTotal += MToonURPFixer.ApplyMyRoomOverrides(root, MyRoomTransparentKeywords);

            if (overrideTotal > 0)
                Debug.Log($"[MToonURPFixerBootstrap] MyRoom 오버라이드: {overrideTotal} 개 머티리얼");
        }
    }
}
