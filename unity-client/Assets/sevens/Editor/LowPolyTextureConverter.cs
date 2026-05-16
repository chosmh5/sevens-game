using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class LowPolyTextureConverter : EditorWindow
{
    private int maxTextureSize = 128;
    private FilterMode filterMode = FilterMode.Point;
    private Vector2 scrollPos;

    // 자동으로 찾은 텍스쳐 폴더 목록
    private List<string> textureFolders = new List<string>();
    private List<bool> folderToggles = new List<bool>();

    [MenuItem("Tools/Low Poly Texture Converter")]
    public static void ShowWindow()
    {
        var window = GetWindow<LowPolyTextureConverter>("Low Poly Converter");
        window.FindTextureFolders();
    }

    private void OnEnable()
    {
        FindTextureFolders();
    }

    private void FindTextureFolders()
    {
        textureFolders.Clear();
        folderToggles.Clear();

        // Assets 하위에서 Textures가 포함된 폴더 + 텍스쳐가 있는 폴더 자동 탐색
        string[] allTexGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets" });
        HashSet<string> folders = new HashSet<string>();

        foreach (string guid in allTexGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            string folder = Path.GetDirectoryName(path).Replace("\\", "/");

            // TextMesh Pro, Editor 등 시스템 폴더 제외
            if (folder.Contains("TextMesh Pro") || folder.Contains("/Editor"))
                continue;

            folders.Add(folder);
        }

        foreach (string folder in folders)
        {
            textureFolders.Add(folder);
            folderToggles.Add(true); // 기본 전체 선택
        }

        textureFolders.Sort();
        // 토글 리스트도 정렬에 맞춰 리셋
        folderToggles.Clear();
        for (int i = 0; i < textureFolders.Count; i++)
            folderToggles.Add(true);
    }

    private void OnGUI()
    {
        GUILayout.Label("Low Poly Texture Settings", EditorStyles.boldLabel);
        GUILayout.Space(10);

        maxTextureSize = EditorGUILayout.IntPopup("Max Texture Size",
            maxTextureSize,
            new string[] { "32", "64", "128", "256", "512" },
            new int[] { 32, 64, 128, 256, 512 });

        filterMode = (FilterMode)EditorGUILayout.EnumPopup("Filter Mode", filterMode);

        GUILayout.Space(10);
        GUILayout.Label("Tip: Point 필터 + 작은 텍스쳐 = 로우폴리 느낌", EditorStyles.helpBox);

        GUILayout.Space(10);
        GUILayout.Label("적용할 텍스쳐 폴더 선택:", EditorStyles.boldLabel);

        if (GUILayout.Button("폴더 다시 검색"))
        {
            FindTextureFolders();
        }

        GUILayout.Space(5);

        // 전체 선택 / 해제
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("전체 선택", GUILayout.Width(80)))
        {
            for (int i = 0; i < folderToggles.Count; i++) folderToggles[i] = true;
        }
        if (GUILayout.Button("전체 해제", GUILayout.Width(80)))
        {
            for (int i = 0; i < folderToggles.Count; i++) folderToggles[i] = false;
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(5);

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos, GUILayout.Height(150));
        for (int i = 0; i < textureFolders.Count; i++)
        {
            // 텍스쳐 개수 표시
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { textureFolders[i] });
            int texCount = 0;
            foreach (string g in guids)
            {
                string p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetDirectoryName(p).Replace("\\", "/") == textureFolders[i])
                    texCount++;
            }

            folderToggles[i] = EditorGUILayout.ToggleLeft(
                $"{textureFolders[i]}  ({texCount}장)", folderToggles[i]);
        }
        EditorGUILayout.EndScrollView();

        GUILayout.Space(10);

        GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
        if (GUILayout.Button("선택 폴더에 로우폴리 적용", GUILayout.Height(40)))
        {
            ApplySettings();
        }
        GUI.backgroundColor = Color.white;

        GUILayout.Space(5);

        GUI.backgroundColor = new Color(0.8f, 0.6f, 0.4f);
        if (GUILayout.Button("선택 폴더 원래대로 복구 (Bilinear, 2048)", GUILayout.Height(30)))
        {
            RevertSettings();
        }
        GUI.backgroundColor = Color.white;
    }

    private List<string> GetSelectedFolders()
    {
        List<string> selected = new List<string>();
        for (int i = 0; i < textureFolders.Count; i++)
        {
            if (folderToggles[i])
                selected.Add(textureFolders[i]);
        }
        return selected;
    }

    private void ApplySettings()
    {
        var folders = GetSelectedFolders();
        if (folders.Count == 0)
        {
            EditorUtility.DisplayDialog("알림", "적용할 폴더를 하나 이상 선택해주세요.", "확인");
            return;
        }

        int count = 0;
        foreach (string folder in folders)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                // 해당 폴더에 직접 있는 것만 (하위폴더 중복 방지)
                if (Path.GetDirectoryName(path).Replace("\\", "/") != folder)
                    continue;

                // 노멀맵 스킵
                if (path.Contains(".normal.") || path.Contains("_nml"))
                    continue;

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                importer.filterMode = filterMode;
                importer.maxTextureSize = maxTextureSize;
                importer.textureCompression = TextureImporterCompression.Uncompressed;

                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                count++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[LowPoly] {folders.Count}개 폴더에서 {count}개 텍스쳐에 설정 적용 완료! (Size: {maxTextureSize}, Filter: {filterMode})");
        EditorUtility.DisplayDialog("완료", $"{count}개 텍스쳐에 로우폴리 설정 적용!", "확인");
    }

    private void RevertSettings()
    {
        var folders = GetSelectedFolders();
        if (folders.Count == 0)
        {
            EditorUtility.DisplayDialog("알림", "복구할 폴더를 하나 이상 선택해주세요.", "확인");
            return;
        }

        int count = 0;
        foreach (string folder in folders)
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);

                if (Path.GetDirectoryName(path).Replace("\\", "/") != folder)
                    continue;

                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                importer.filterMode = FilterMode.Bilinear;
                importer.maxTextureSize = 2048;
                importer.textureCompression = TextureImporterCompression.Compressed;

                EditorUtility.SetDirty(importer);
                importer.SaveAndReimport();
                count++;
            }
        }

        AssetDatabase.Refresh();
        Debug.Log($"[LowPoly] {count}개 텍스쳐 원래 설정으로 복구 완료!");
        EditorUtility.DisplayDialog("완료", $"{count}개 텍스쳐 원래대로 복구!", "확인");
    }
}
