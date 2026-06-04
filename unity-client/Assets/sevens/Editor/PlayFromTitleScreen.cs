using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Play 버튼을 누르면 현재 열린 씬과 무관하게 항상 TitleScreen부터 시작하도록 강제한다.
/// (Unity의 EditorSceneManager.playModeStartScene 사용)
///
/// 기본 ON. 끄려면 메뉴 Tools ▸ Sevens ▸ Play Always Starts at TitleScreen 토글.
/// 에디터를 재시작하거나 스크립트가 재컴파일돼도 설정이 유지된다(EditorPrefs).
/// </summary>
[InitializeOnLoad]
public static class PlayFromTitleScreen
{
    private const string Pref           = "sevens.playFromTitle";
    private const string MenuPath       = "Tools/Sevens/Play Always Starts at TitleScreen";
    private const string TitleScenePath = "Assets/sevens/Scenes/TitleScreen.unity";

    static PlayFromTitleScreen()
    {
        // 에디터 로드/재컴파일 직후 적용(에셋 DB 준비 후).
        EditorApplication.delayCall += Apply;
    }

    private static bool Enabled
    {
        get => EditorPrefs.GetBool(Pref, true); // 기본 ON
        set => EditorPrefs.SetBool(Pref, value);
    }

    private static void Apply()
    {
        if (Enabled)
        {
            var sceneAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(TitleScenePath);
            if (sceneAsset != null)
                EditorSceneManager.playModeStartScene = sceneAsset;
            else
                Debug.LogWarning($"[Sevens] TitleScreen 씬을 찾지 못했습니다: {TitleScenePath}");
        }
        else
        {
            EditorSceneManager.playModeStartScene = null; // 기본 동작(현재 열린 씬)으로 복귀
        }
    }

    [MenuItem(MenuPath, priority = 12)]
    private static void Toggle()
    {
        Enabled = !Enabled;
        Apply();
        Debug.Log($"[Sevens] Play 시작 = TitleScreen : {(Enabled ? "ON" : "OFF")}");
    }

    [MenuItem(MenuPath, validate = true)]
    private static bool ToggleValidate()
    {
        Menu.SetChecked(MenuPath, Enabled);
        return true;
    }
}
