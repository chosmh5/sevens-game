using UnityEngine;
using UnityEditor;

/// <summary>
/// 글로벌 셰이더 키워드를 강제 리셋하는 도구
/// WorldSpaceTransitions의 FadingTransition이 남긴 FADE_SPHERE 등을 정리할 때 사용
/// </summary>
public class ShaderKeywordReset
{
    [MenuItem("Tools/셰이더 키워드 리셋")]
    public static void ResetKeywords()
    {
        // 글로벌 셰이더 키워드 비활성화
        Shader.DisableKeyword("FADE_SPHERE");
        Shader.DisableKeyword("FADE_PLANE");
        Shader.DisableKeyword("CLIP_SPHERE");
        Shader.DisableKeyword("CLIP_PLANE");
        Shader.SetGlobalFloat("_Radius", 0f);
        Shader.SetGlobalFloat("_spread", 0f);

        // FadingTransition 제거 (글로벌 키워드 오염 원인)
        var ft = Object.FindAnyObjectByType<WorldSpaceTransitions.FadingTransition>();
        if (ft != null)
        {
            Undo.DestroyObjectImmediate(ft.gameObject);
            Debug.Log("[Reset] FadingTransition 삭제됨");
        }

        Debug.Log("[Reset] 글로벌 셰이더 키워드 리셋 완료!");
        EditorUtility.DisplayDialog("완료", "셰이더 키워드가 리셋되었습니다.\n건물이 다시 보일 것입니다.", "확인");
    }
}
