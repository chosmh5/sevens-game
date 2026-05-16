using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
using System.Collections.Generic;

/// <summary>
/// 씬 관리 도우미
/// - sevens.unity → MyRoom.unity 이름 변경 및 이동
/// - CardGame 빈 씬 생성
/// - Build Profiles(EditorBuildSettings)에 씬 등록
/// </summary>
public class SceneSetupHelper : EditorWindow
{
    [MenuItem("Tools/씬 구성 도우미")]
    public static void ShowWindow()
    {
        GetWindow<SceneSetupHelper>("씬 구성 도우미");
    }

    private void OnGUI()
    {
        GUILayout.Label("씬 구성 도우미", EditorStyles.boldLabel);
        GUILayout.Space(10);

        // 현재 상태 표시
        EditorGUILayout.LabelField("현재 씬 파일 상태:", EditorStyles.boldLabel);

        string lobbyPath = "Assets/sevens/Scenes/Lobby.unity";
        string myRoomPath = "Assets/sevens/Scenes/MyRoom.unity";
        string cardGamePath = "Assets/sevens/Scenes/CardGame.unity";
        string oldSevensPath = "Assets/sevens.unity";

        ShowStatus("Lobby.unity", File.Exists(GetFullPath(lobbyPath)));
        ShowStatus("MyRoom.unity", File.Exists(GetFullPath(myRoomPath)));
        ShowStatus("CardGame.unity", File.Exists(GetFullPath(cardGamePath)));

        if (File.Exists(GetFullPath(oldSevensPath)))
        {
            EditorGUILayout.HelpBox("sevens.unity가 Assets 루트에 있습니다. MyRoom으로 이름 변경이 필요합니다.", MessageType.Warning);
        }

        GUILayout.Space(15);

        // 1단계: sevens.unity → MyRoom.unity
        EditorGUILayout.LabelField("1단계: sevens.unity → MyRoom.unity", EditorStyles.boldLabel);
        if (File.Exists(GetFullPath(oldSevensPath)))
        {
            if (GUILayout.Button("sevens.unity를 MyRoom.unity로 이동", GUILayout.Height(35)))
            {
                RenameAndMoveScene();
            }
        }
        else if (File.Exists(GetFullPath(myRoomPath)))
        {
            EditorGUILayout.HelpBox("✓ MyRoom.unity 이미 존재합니다.", MessageType.Info);
        }
        else
        {
            EditorGUILayout.HelpBox("sevens.unity를 찾을 수 없습니다.", MessageType.Error);
        }

        GUILayout.Space(10);

        // 2단계: CardGame 씬 생성
        EditorGUILayout.LabelField("2단계: CardGame 씬 생성", EditorStyles.boldLabel);
        if (!File.Exists(GetFullPath(cardGamePath)))
        {
            if (GUILayout.Button("빈 CardGame.unity 생성", GUILayout.Height(35)))
            {
                CreateCardGameScene();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("✓ CardGame.unity 이미 존재합니다.", MessageType.Info);
        }

        GUILayout.Space(10);

        // 3단계: Build Settings 등록
        EditorGUILayout.LabelField("3단계: Build Settings에 씬 등록", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "현재 Build Settings 씬 목록:\n" + GetBuildSceneList(),
            MessageType.None);

        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("Lobby → MyRoom → CardGame 순서로 등록", GUILayout.Height(40)))
        {
            RegisterBuildScenes();
        }
        GUI.backgroundColor = Color.white;
    }

    private void ShowStatus(string name, bool exists)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(exists ? "✓" : "✗", GUILayout.Width(20));
        EditorGUILayout.LabelField(name);
        EditorGUILayout.EndHorizontal();
    }

    private void RenameAndMoveScene()
    {
        // 현재 씬 저장
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        string scenesFolder = "Assets/sevens/Scenes";
        if (!AssetDatabase.IsValidFolder(scenesFolder))
        {
            AssetDatabase.CreateFolder("Assets/sevens", "Scenes");
        }

        string result = AssetDatabase.MoveAsset("Assets/sevens.unity", "Assets/sevens/Scenes/MyRoom.unity");
        if (string.IsNullOrEmpty(result))
        {
            Debug.Log("[SceneSetup] sevens.unity → Assets/sevens/Scenes/MyRoom.unity 이동 완료!");
            AssetDatabase.Refresh();

            // 이동한 씬 다시 열기
            EditorSceneManager.OpenScene("Assets/sevens/Scenes/MyRoom.unity");
        }
        else
        {
            Debug.LogError($"[SceneSetup] 이동 실패: {result}");
        }
    }

    private void CreateCardGameScene()
    {
        string scenesFolder = "Assets/sevens/Scenes";
        if (!AssetDatabase.IsValidFolder(scenesFolder))
        {
            AssetDatabase.CreateFolder("Assets/sevens", "Scenes");
        }

        // 현재 씬 저장
        EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();

        // 새 빈 씬 생성
        var newScene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

        // 기본 카메라 설정
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.backgroundColor = new Color(0.1f, 0.15f, 0.2f);
        }

        // "CardGame" 안내 텍스트 (나중에 교체)
        GameObject placeholder = new GameObject("CardGame_Placeholder");
        placeholder.transform.position = Vector3.zero;

        // BackToLobby 버튼 캔버스
        GameObject canvas = new GameObject("Canvas");
        var canvasComp = canvas.AddComponent<Canvas>();
        canvasComp.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.AddComponent<UnityEngine.UI.CanvasScaler>();
        canvas.AddComponent<UnityEngine.UI.GraphicRaycaster>();

        // 뒤로가기 버튼
        GameObject backBtnObj = new GameObject("BackToLobbyButton");
        backBtnObj.transform.SetParent(canvas.transform);
        var rectTransform = backBtnObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 1);
        rectTransform.anchorMax = new Vector2(0, 1);
        rectTransform.pivot = new Vector2(0, 1);
        rectTransform.anchoredPosition = new Vector2(20, -20);
        rectTransform.sizeDelta = new Vector2(120, 50);

        var btnImage = backBtnObj.AddComponent<UnityEngine.UI.Image>();
        btnImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

        var button = backBtnObj.AddComponent<UnityEngine.UI.Button>();

        // 버튼 텍스트
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(backBtnObj.transform);
        var textRect = textObj.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.sizeDelta = Vector2.zero;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        var tmpText = textObj.AddComponent<TMPro.TextMeshProUGUI>();
        tmpText.text = "← 로비";
        tmpText.fontSize = 24;
        tmpText.alignment = TMPro.TextAlignmentOptions.Center;

        // BackToLobbyButton 컴포넌트
        var backScript = backBtnObj.AddComponent<BackToLobbyButton>();
        // SerializedField이므로 직접 할당
        var so = new SerializedObject(backScript);
        so.FindProperty("backButton").objectReferenceValue = button;
        so.ApplyModifiedProperties();

        // 씬 저장
        EditorSceneManager.SaveScene(newScene, "Assets/sevens/Scenes/CardGame.unity");
        Debug.Log("[SceneSetup] CardGame.unity 생성 완료!");

        // 원래 씬으로 돌아가기
        if (File.Exists(Application.dataPath + "/sevens/Scenes/MyRoom.unity"))
            EditorSceneManager.OpenScene("Assets/sevens/Scenes/MyRoom.unity");
        else if (File.Exists(Application.dataPath + "/../Assets/sevens.unity"))
            EditorSceneManager.OpenScene("Assets/sevens.unity");
    }

    private void RegisterBuildScenes()
    {
        var scenes = new List<EditorBuildSettingsScene>();

        string[] scenePaths = {
            "Assets/sevens/Scenes/Lobby.unity",
            "Assets/sevens/Scenes/MyRoom.unity",
            "Assets/sevens/Scenes/CardGame.unity"
        };

        foreach (string path in scenePaths)
        {
            if (File.Exists(GetFullPath(path)))
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
                Debug.Log($"[SceneSetup] 등록: {path}");
            }
            else
            {
                Debug.LogWarning($"[SceneSetup] 파일 없음: {path}");
            }
        }

        EditorBuildSettings.scenes = scenes.ToArray();
        Debug.Log($"[SceneSetup] Build Settings에 {scenes.Count}개 씬 등록 완료!");
    }

    private string GetBuildSceneList()
    {
        var scenes = EditorBuildSettings.scenes;
        if (scenes.Length == 0) return "(비어있음)";

        string list = "";
        for (int i = 0; i < scenes.Length; i++)
        {
            list += $"{i}: {scenes[i].path} [{(scenes[i].enabled ? "활성" : "비활성")}]\n";
        }
        return list.TrimEnd();
    }

    private string GetFullPath(string assetPath)
    {
        return Path.Combine(Application.dataPath, "..", assetPath);
    }
}
