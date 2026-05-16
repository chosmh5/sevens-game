using UnityEditor;
using UnityEngine;

/// <summary>
/// Resources/Cards 폴더의 PNG 파일들이 Sprite(2D and UI) 타입으로 임포트되도록 강제.
/// AssetPostprocessor를 사용해 임포트 시 자동 적용 + 메뉴에서 일괄 재임포트도 가능.
/// </summary>
public class CardTextureImporter : AssetPostprocessor
{
    private const string CardFolder = "Assets/sevens/Resources/Cards/";

    private void OnPreprocessTexture()
    {
        if (!assetPath.StartsWith(CardFolder)) return;

        var importer = (TextureImporter)assetImporter;
        if (importer.textureType == TextureImporterType.Sprite) return; // 이미 OK

        importer.textureType         = TextureImporterType.Sprite;
        importer.spriteImportMode    = SpriteImportMode.Single;
        importer.mipmapEnabled       = false;
        importer.filterMode          = FilterMode.Bilinear;
        importer.alphaIsTransparency = true;
        importer.spritePixelsPerUnit = 100f;
    }

    [MenuItem("Tools/Sevens/카드 텍스처 일괄 Sprite 변환")]
    public static void ReimportAllCards()
    {
        if (!AssetDatabase.IsValidFolder(CardFolder.TrimEnd('/')))
        {
            Debug.LogError("[CardTextureImporter] " + CardFolder + " 폴더가 없습니다.");
            return;
        }

        var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { CardFolder.TrimEnd('/') });
        int count = 0;
        foreach (var guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) continue;

            bool changed = false;
            if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
            if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; changed = true; }
            if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }
            if (!importer.alphaIsTransparency) { importer.alphaIsTransparency = true; changed = true; }

            if (changed)
            {
                importer.SaveAndReimport();
                count++;
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[CardTextureImporter] {count}개 카드 텍스처를 Sprite로 재임포트했습니다.");
    }
}
