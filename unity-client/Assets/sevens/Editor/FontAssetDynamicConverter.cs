#if UNITY_EDITOR
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// TMP 정적(Static) 폰트 에셋을 Dynamic으로 바꿔 용량을 줄이는 에디터 도구.
///
/// 왜 필요한가:
///   NotoSansKR-Regular SDF.asset 은 한글 11,172자 전부를 8192×8192 아틀라스에 미리 구워 둔
///   정적 에셋이라 133MB나 된다. GitHub 100MB 제한에 걸리고, APK에도 그대로 들어간다.
///   Dynamic 모드는 실제로 화면에 나온 글자만 런타임에 아틀라스에 추가하므로
///   에셋 파일은 수백 KB 수준, 런타임 메모리도 2048 아틀라스 몇 장이면 충분하다.
///   (AI 대사처럼 어떤 글자가 나올지 모르는 텍스트에도 그대로 대응된다.)
///
/// 하는 일:
///   1) 원본을 unity-client/FontBackup/ 에 백업(최초 1회, git 제외 폴더)
///   2) Atlas Population Mode = Dynamic, 원본 TTF 연결
///   3) 아틀라스 2048×2048 + Multi Atlas 허용, 빌드 시 동적 데이터 비우기
///   4) 기존 글리프/아틀라스 데이터 삭제 → 저장
///
/// 메뉴: Tools > Sevens > 폰트 Dynamic 변환
/// </summary>
public static class FontAssetDynamicConverter
{
    private const string NotoPath =
        "Assets/TextMesh Pro/Fonts/Noto_Sans_KR/static/NotoSansKR-Regular SDF.asset";
    private const string BackupDir = "FontBackup"; // unity-client/FontBackup (Assets 밖)
    private const int AtlasSize = 2048;

    [MenuItem("Tools/Sevens/폰트 Dynamic 변환 (NotoSansKR)")]
    public static void ConvertNoto()
    {
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(NotoPath);
        if (font == null)
        {
            EditorUtility.DisplayDialog("폰트 변환", "폰트 에셋을 찾지 못했습니다:\n" + NotoPath, "확인");
            return;
        }
        Convert(font);
    }

    [MenuItem("Tools/Sevens/폰트 Dynamic 변환 (선택한 폰트 에셋)")]
    public static void ConvertSelected()
    {
        int n = 0;
        foreach (var obj in Selection.objects)
            if (obj is TMP_FontAsset fa && Convert(fa)) n++;
        if (n == 0)
            EditorUtility.DisplayDialog("폰트 변환", "Project 창에서 TMP 폰트 에셋(SDF)을 선택한 뒤 실행하세요.", "확인");
    }

    [MenuItem("Tools/Sevens/폰트 Dynamic 변환 (선택한 폰트 에셋)", true)]
    private static bool ConvertSelectedValidate()
    {
        foreach (var obj in Selection.objects)
            if (obj is TMP_FontAsset) return true;
        return false;
    }

    private static bool Convert(TMP_FontAsset font)
    {
        string path = AssetDatabase.GetAssetPath(font);
        long before = FileSize(path);

        if (font.atlasPopulationMode == AtlasPopulationMode.Dynamic && before < 20L * 1024 * 1024)
        {
            Debug.Log($"[FontConverter] 이미 Dynamic 입니다: {path} ({Mb(before)})");
            return false;
        }

        // ── 원본 TTF 찾기 (m_SourceFontFileGUID) ──
        var so = new SerializedObject(font);
        string guid = so.FindProperty("m_SourceFontFileGUID")?.stringValue;
        Font ttf = string.IsNullOrEmpty(guid)
            ? null
            : AssetDatabase.LoadAssetAtPath<Font>(AssetDatabase.GUIDToAssetPath(guid));
        if (ttf == null)
        {
            EditorUtility.DisplayDialog("폰트 변환",
                "원본 폰트 파일(.ttf/.otf)을 찾지 못해 Dynamic으로 바꿀 수 없습니다.\n" + path, "확인");
            return false;
        }

        if (!EditorUtility.DisplayDialog("폰트 Dynamic 변환",
                $"{Path.GetFileName(path)} ({Mb(before)})\n\n" +
                $"원본 TTF: {Path.GetFileName(AssetDatabase.GetAssetPath(ttf))}\n" +
                $"아틀라스 {AtlasSize}×{AtlasSize}, Multi Atlas 허용으로 바꿉니다.\n" +
                "원본은 unity-client/FontBackup/ 에 백업됩니다.",
                "변환", "취소"))
            return false;

        // ── 1) 백업 ──
        try
        {
            Directory.CreateDirectory(BackupDir);
            string backup = Path.Combine(BackupDir, Path.GetFileName(path) + ".bak");
            if (!File.Exists(backup)) File.Copy(path, backup);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[FontConverter] 백업 실패(변환은 계속): " + e.Message);
        }

        // ── 2)~3) 설정 변경 ──
        so.FindProperty("m_AtlasPopulationMode").intValue = (int)AtlasPopulationMode.Dynamic;
        so.FindProperty("m_SourceFontFile").objectReferenceValue = ttf;
        so.FindProperty("m_AtlasWidth").intValue = AtlasSize;
        so.FindProperty("m_AtlasHeight").intValue = AtlasSize;
        so.FindProperty("m_IsMultiAtlasTexturesEnabled").boolValue = true;
        so.FindProperty("m_ClearDynamicDataOnBuild").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (font.material != null)
        {
            if (font.material.HasProperty("_TextureWidth"))
                font.material.SetFloat("_TextureWidth", AtlasSize);
            if (font.material.HasProperty("_TextureHeight"))
                font.material.SetFloat("_TextureHeight", AtlasSize);
            EditorUtility.SetDirty(font.material);
        }

        // ── 4) 기존 글리프·아틀라스 비우기 → 저장 ──
        font.ClearFontAssetData(true);
        EditorUtility.SetDirty(font);
        if (font.atlasTexture != null) EditorUtility.SetDirty(font.atlasTexture);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        long after = FileSize(path);
        Debug.Log($"[FontConverter] 변환 완료: {path}  {Mb(before)} → {Mb(after)}");
        EditorUtility.DisplayDialog("폰트 변환 완료",
            $"{Path.GetFileName(path)}\n{Mb(before)} → {Mb(after)}\n\n" +
            "씬을 열어 한글이 정상으로 보이는지 확인하세요.\n" +
            "문제가 있으면 FontBackup 폴더의 .bak 파일로 되돌릴 수 있습니다.",
            "확인");
        return true;
    }

    private static long FileSize(string assetPath)
    {
        try { return new FileInfo(assetPath).Length; }
        catch { return 0; }
    }

    private static string Mb(long bytes) => $"{bytes / 1048576f:0.#}MB";
}
#endif
