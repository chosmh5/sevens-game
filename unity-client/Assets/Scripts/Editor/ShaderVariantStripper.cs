using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 빌드 시 불필요한 셰이더 variant를 제거하여 빌드 시간을 크게 단축합니다.
/// TMP_SDF-URP Lit, HDRP 셰이더 등 사용하지 않는 무거운 셰이더를 스트리핑합니다.
/// </summary>
public class ShaderVariantStripper : IPreprocessShaders
{
    // 빌드에서 완전히 제외할 셰이더 이름 목록
    private static readonly HashSet<string> s_ShadersToStrip = new HashSet<string>
    {
        "TextMeshPro/SRP/TMP_SDF-URP Lit",
        "TextMeshPro/SRP/TMP_SDF-HDRP LIT",
        "TextMeshPro/SRP/TMP_SDF-HDRP UNLIT",
        "Hidden/Universal Render Pipeline/Debug",
    };

    // 셰이더 이름에 포함되면 제외할 키워드
    private static readonly string[] s_PartialNamesToStrip = new string[]
    {
        "HDRP",
        "InternalErrorShader",
    };

    public int callbackOrder => 0;

    public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> data)
    {
        string shaderName = shader.name;

        // 완전 일치로 스트리핑
        if (s_ShadersToStrip.Contains(shaderName))
        {
            data.Clear();
            return;
        }

        // 부분 일치로 스트리핑
        for (int i = 0; i < s_PartialNamesToStrip.Length; i++)
        {
            if (shaderName.Contains(s_PartialNamesToStrip[i]))
            {
                data.Clear();
                return;
            }
        }
    }
}
