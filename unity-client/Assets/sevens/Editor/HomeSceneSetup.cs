using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 홈(마이룸) 씬 준비.
/// - Assets/sevens/Scenes/Home.unity 생성([Home] + HomeController, 세비-호 프리팹 연결)
/// - 빌드 순서: Home 을 맨 앞(앱 첫 화면) → CardGame → 나머지(로비·타이틀·옛 마이룸은 그대로 둠)
/// - 기본 화면 방향: 세로(카드게임 들어갈 때 코드로 가로 전환)
///
/// Home.unity 가 없으면 에디터가 켜질 때 한 번 자동으로 실행된다.
/// 다시 하려면 메뉴 Tools ▸ Sevens ▸ 홈 씬 만들기 / 홈 설정 다시 적용.
/// </summary>
[InitializeOnLoad]
public static class HomeSceneSetup
{
    public const string ScenePath  = "Assets/sevens/Scenes/Home.unity";
    private const string PrefabPath = "Assets/seven.prefab";

    static HomeSceneSetup()
    {
        EditorApplication.delayCall += AutoSetup;
    }

    private static void AutoSetup()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (File.Exists(ScenePath)) return;           // 이미 만들었으면 손대지 않는다
        if (CreateHomeScene())
        {
            ApplyProjectSettings();
            Debug.Log("[HomeSceneSetup] 홈 씬을 만들고 첫 화면(세로)으로 설정했습니다. " +
                      "되돌리려면 File ▸ Build Profiles 에서 씬 순서를 바꾸세요.");
        }
    }

    [MenuItem("Tools/Sevens/홈 씬 만들기 (덮어쓰기)", priority = 1)]
    private static void CreateMenu()
    {
        if (File.Exists(ScenePath) &&
            !EditorUtility.DisplayDialog("홈 씬", "Home.unity 가 이미 있어요. 새로 만들까요?", "새로 만들기", "취소"))
            return;
        if (CreateHomeScene()) ApplyProjectSettings();
    }

    [MenuItem("Tools/Sevens/홈 설정 다시 적용 (빌드 순서·세로 화면)", priority = 2)]
    private static void ApplyMenu()
    {
        if (!File.Exists(ScenePath)) { CreateMenu(); return; }
        ApplyProjectSettings();
    }

    /// <summary>Home.unity 를 따로(Additive) 만들고 저장 후 닫는다 — 지금 열린 씬은 건드리지 않는다.</summary>
    private static bool CreateHomeScene()
    {
        Scene scene = default;
        try
        {
            scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            var go = new GameObject("[Home]");
            SceneManager.MoveGameObjectToScene(go, scene);
            var hc = go.AddComponent<HomeController>();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) Debug.LogWarning($"[HomeSceneSetup] {PrefabPath} 를 찾지 못했어요. [Home] 의 Character Prefab 을 직접 넣어 주세요.");
            var so = new SerializedObject(hc);
            so.FindProperty("characterPrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            bool ok = EditorSceneManager.SaveScene(scene, ScenePath);
            EditorSceneManager.CloseScene(scene, true);
            AssetDatabase.Refresh();
            return ok;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[HomeSceneSetup] 홈 씬 자동 생성 실패: {e.Message}\n" +
                             "열린 씬을 저장한 뒤 메뉴 Tools ▸ Sevens ▸ 홈 씬 만들기를 실행하세요.");
            if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            return false;
        }
    }

    private static void ApplyProjectSettings()
    {
        // 1) 빌드 순서: Home → CardGame → 나머지
        var existing = EditorBuildSettings.scenes.ToList();
        var ordered = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
        var card = existing.FirstOrDefault(s => s.path.EndsWith("/CardGame.unity"));
        if (card != null) ordered.Add(new EditorBuildSettingsScene(card.path, true));
        foreach (var s in existing)
            if (s.path != ScenePath && (card == null || s.path != card.path)) ordered.Add(s);
        EditorBuildSettings.scenes = ordered.ToArray();

        // 2) 앱은 세로로 시작(카드게임은 GameSceneManager 가 가로로 돌린다)
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

        // 3) 에디터 Play 도 홈부터
        PlayFromTitleScreen.Apply();

        AssetDatabase.SaveAssets();
        Debug.Log("[HomeSceneSetup] 빌드 첫 씬 = Home, 기본 방향 = 세로");
    }
}
