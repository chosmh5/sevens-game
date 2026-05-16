using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

/// <summary>
/// 사이버펑크 글리치 풀스크린 효과 - URP Renderer Feature
/// - 색수차 (Chromatic Aberration)
/// - 스캔라인
/// - 랜덤 글리치 블록
/// - 비네트 (보라빛)
/// - 따뜻한 틴트
///
/// 사용법: URP Renderer Data에 이 Feature를 추가
/// </summary>
public class CyberpunkGlitchFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
        public Material glitchMaterial;

        [Header("Chromatic Aberration")]
        [Range(0, 0.02f)] public float chromaStrength = 0.003f;
        [Range(0, 5f)] public float chromaPulse = 1.5f;

        [Header("Scanlines")]
        [Range(50, 800)] public float scanlineCount = 300;
        [Range(0, 0.3f)] public float scanlineAlpha = 0.05f;

        [Header("Glitch")]
        [Range(0, 0.05f)] public float glitchIntensity = 0.01f;
        [Range(0, 20)] public float glitchSpeed = 8f;
        [Range(2, 64)] public float glitchBlockSize = 16f;

        [Header("Vignette")]
        [Range(0, 1)] public float vignetteIntensity = 0.3f;
        public Color vignetteColor = new Color(0.05f, 0.02f, 0.1f);

        [Header("Pixelation")]
        [Range(1, 16)] public float pixelSize = 4f;
        [Range(0, 1)] public float pixelSmooth = 0.8f;

        [Header("Warm Tint")]
        public Color tintColor = new Color(1f, 0.95f, 0.9f);
        [Range(0, 0.5f)] public float tintStrength = 0.1f;
    }

    public Settings settings = new Settings();
    private CyberpunkGlitchPass glitchPass;

    public override void Create()
    {
        glitchPass = new CyberpunkGlitchPass(settings);
        glitchPass.renderPassEvent = settings.renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (settings.glitchMaterial == null) return;
        renderer.EnqueuePass(glitchPass);
    }

    class CyberpunkGlitchPass : ScriptableRenderPass
    {
        private Settings settings;
        private static readonly int ChromaStrengthId = Shader.PropertyToID("_ChromaStrength");
        private static readonly int ChromaPulseId = Shader.PropertyToID("_ChromaPulse");
        private static readonly int ScanlineCountId = Shader.PropertyToID("_ScanlineCount");
        private static readonly int ScanlineAlphaId = Shader.PropertyToID("_ScanlineAlpha");
        private static readonly int GlitchIntensityId = Shader.PropertyToID("_GlitchIntensity");
        private static readonly int GlitchSpeedId = Shader.PropertyToID("_GlitchSpeed");
        private static readonly int GlitchBlockSizeId = Shader.PropertyToID("_GlitchBlockSize");
        private static readonly int VignetteIntensityId = Shader.PropertyToID("_VignetteIntensity");
        private static readonly int VignetteColorId = Shader.PropertyToID("_VignetteColor");
        private static readonly int PixelSizeId = Shader.PropertyToID("_PixelSize");
        private static readonly int PixelSmoothId = Shader.PropertyToID("_PixelSmooth");
        private static readonly int TintColorId = Shader.PropertyToID("_TintColor");
        private static readonly int TintStrengthId = Shader.PropertyToID("_TintStrength");

        public CyberpunkGlitchPass(Settings settings)
        {
            this.settings = settings;
        }

        [System.Obsolete]
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (settings.glitchMaterial == null) return;

            CommandBuffer cmd = CommandBufferPool.Get("CyberpunkGlitch");

            // 파라미터 업데이트
            Material mat = settings.glitchMaterial;
            mat.SetFloat(ChromaStrengthId, settings.chromaStrength);
            mat.SetFloat(ChromaPulseId, settings.chromaPulse);
            mat.SetFloat(ScanlineCountId, settings.scanlineCount);
            mat.SetFloat(ScanlineAlphaId, settings.scanlineAlpha);
            mat.SetFloat(GlitchIntensityId, settings.glitchIntensity);
            mat.SetFloat(GlitchSpeedId, settings.glitchSpeed);
            mat.SetFloat(GlitchBlockSizeId, settings.glitchBlockSize);
            mat.SetFloat(VignetteIntensityId, settings.vignetteIntensity);
            mat.SetColor(VignetteColorId, settings.vignetteColor);
            mat.SetFloat(PixelSizeId, settings.pixelSize);
            mat.SetFloat(PixelSmoothId, settings.pixelSmooth);
            mat.SetColor(TintColorId, settings.tintColor);
            mat.SetFloat(TintStrengthId, settings.tintStrength);

            // Blit 풀스크린
            var source = renderingData.cameraData.renderer.cameraColorTargetHandle;
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;

            int tempId = Shader.PropertyToID("_TempGlitchTex");
            cmd.GetTemporaryRT(tempId, desc);
            cmd.Blit(source, tempId);
            cmd.Blit(tempId, source, mat);
            cmd.ReleaseTemporaryRT(tempId);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}
